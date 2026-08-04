import base64
import binascii
import hashlib
import re
from contextlib import asynccontextmanager

from fastapi import Depends, FastAPI, Header, HTTPException, Response, status
from psycopg.errors import UniqueViolation

from .database import connect, initialize
from .models import RestoreResult, SnapshotPut
from .security import authenticated_steam_id


CAREER_PATTERN = re.compile(r"^[A-Za-z0-9._ -]{1,160}$")
MAX_SNAPSHOT_BYTES = 5 * 1024 * 1024


@asynccontextmanager
async def lifespan(_: FastAPI):
    initialize()
    yield


app = FastAPI(
    title="Schedule I Backpack API",
    version="1.0.0",
    lifespan=lifespan,
    docs_url=None,
    redoc_url=None,
    openapi_url=None,
)


def validate_career(career_id: str) -> str:
    if not CAREER_PATTERN.fullmatch(career_id):
        raise HTTPException(status_code=400, detail="Invalid career ID")
    return career_id


def expected_revision(if_match: str | None) -> int:
    if if_match is None:
        raise HTTPException(status_code=428, detail="If-Match with the current revision is required")
    value = if_match.removeprefix("W/").strip().strip('"')
    if not value.isdigit():
        raise HTTPException(status_code=400, detail="If-Match must contain the current numeric revision")
    return int(value)


@app.get("/health")
def health():
    with connect() as connection:
        connection.execute("SELECT 1").fetchone()
    return {"status": "healthy", "service": "schedule-i-backpack", "schema": 1}


@app.get("/v1/backpacks/{career_id}")
def get_backpack(career_id: str, response: Response, steam_id: int = Depends(authenticated_steam_id)):
    career_id = validate_career(career_id)
    with connect() as connection:
        row = connection.execute(
            """
            SELECT r.revision, r.content_hash, r.transaction_tail_hash, r.snapshot, r.created_at
            FROM backpack_heads h
            JOIN backpack_revisions r USING (steam_id, career_id, revision)
            WHERE h.steam_id = %s AND h.career_id = %s
            """,
            (steam_id, career_id),
        ).fetchone()
    if row is None:
        raise HTTPException(status_code=404, detail="No cloud backpack exists for this career")
    response.headers["ETag"] = f'"{row[0]}"'
    return {
        "revision": row[0],
        "contentHash": row[1],
        "transactionTailHash": row[2],
        "snapshotBase64": base64.b64encode(row[3]).decode("ascii"),
        "createdAt": row[4],
    }


@app.put("/v1/backpacks/{career_id}", status_code=status.HTTP_201_CREATED)
def put_backpack(
    career_id: str,
    body: SnapshotPut,
    response: Response,
    if_match: str | None = Header(default=None, alias="If-Match"),
    steam_id: int = Depends(authenticated_steam_id),
):
    career_id = validate_career(career_id)
    expected = expected_revision(if_match)
    try:
        snapshot = base64.b64decode(body.snapshotBase64, validate=True)
    except (binascii.Error, ValueError):
        raise HTTPException(status_code=400, detail="snapshotBase64 is invalid")
    if len(snapshot) > MAX_SNAPSHOT_BYTES:
        raise HTTPException(status_code=413, detail="Snapshot exceeds 5 MB")
    actual_hash = hashlib.sha256(snapshot).hexdigest().upper()
    if actual_hash != body.contentHash:
        raise HTTPException(status_code=400, detail="Snapshot content hash does not match")

    with connect() as connection:
        with connection.transaction():
            head = connection.execute(
                "SELECT revision FROM backpack_heads WHERE steam_id = %s AND career_id = %s FOR UPDATE",
                (steam_id, career_id),
            ).fetchone()
            current = int(head[0]) if head else 0
            if current != expected:
                raise HTTPException(status_code=409, detail={"message": "Revision conflict", "currentRevision": current})
            if body.revision != current + 1:
                raise HTTPException(status_code=400, detail=f"Next revision must be {current + 1}")
            try:
                connection.execute(
                    """
                    INSERT INTO backpack_revisions
                      (steam_id, career_id, revision, content_hash, transaction_tail_hash, snapshot)
                    VALUES (%s, %s, %s, %s, %s, %s)
                    """,
                    (steam_id, career_id, body.revision, body.contentHash, body.transactionTailHash, snapshot),
                )
            except UniqueViolation:
                raise HTTPException(status_code=409, detail="That revision already exists")
            connection.execute(
                """
                INSERT INTO backpack_heads (steam_id, career_id, revision, content_hash)
                VALUES (%s, %s, %s, %s)
                ON CONFLICT (steam_id, career_id) DO UPDATE SET
                  revision = EXCLUDED.revision,
                  content_hash = EXCLUDED.content_hash,
                  updated_at = now()
                """,
                (steam_id, career_id, body.revision, body.contentHash),
            )
    response.headers["ETag"] = f'"{body.revision}"'
    return {"revision": body.revision, "contentHash": body.contentHash}


@app.get("/v1/backpacks/{career_id}/history")
def history(career_id: str, steam_id: int = Depends(authenticated_steam_id)):
    career_id = validate_career(career_id)
    with connect() as connection:
        rows = connection.execute(
            """
            SELECT revision, content_hash, transaction_tail_hash, created_at, restored_from
            FROM backpack_revisions
            WHERE steam_id = %s AND career_id = %s
            ORDER BY revision DESC LIMIT 100
            """,
            (steam_id, career_id),
        ).fetchall()
    return [{
        "revision": row[0], "contentHash": row[1], "transactionTailHash": row[2],
        "createdAt": row[3], "restoredFrom": row[4]
    } for row in rows]


@app.post("/v1/backpacks/{career_id}/restore/{source_revision}", response_model=RestoreResult)
def restore(career_id: str, source_revision: int, steam_id: int = Depends(authenticated_steam_id)):
    career_id = validate_career(career_id)
    with connect() as connection:
        with connection.transaction():
            head = connection.execute(
                "SELECT revision FROM backpack_heads WHERE steam_id = %s AND career_id = %s FOR UPDATE",
                (steam_id, career_id),
            ).fetchone()
            if head is None:
                raise HTTPException(status_code=404, detail="No cloud backpack exists for this career")
            source = connection.execute(
                """
                SELECT content_hash, transaction_tail_hash, snapshot FROM backpack_revisions
                WHERE steam_id = %s AND career_id = %s AND revision = %s
                """,
                (steam_id, career_id, source_revision),
            ).fetchone()
            if source is None:
                raise HTTPException(status_code=404, detail="Recovery revision was not found")
            revision = int(head[0]) + 1
            connection.execute(
                """
                INSERT INTO backpack_revisions
                  (steam_id, career_id, revision, content_hash, transaction_tail_hash, snapshot, restored_from)
                VALUES (%s, %s, %s, %s, %s, %s, %s)
                """,
                (steam_id, career_id, revision, source[0], source[1], source[2], source_revision),
            )
            connection.execute(
                """
                UPDATE backpack_heads SET revision = %s, content_hash = %s, updated_at = now()
                WHERE steam_id = %s AND career_id = %s
                """,
                (revision, source[0], steam_id, career_id),
            )
    return RestoreResult(revision=revision, contentHash=source[0])
