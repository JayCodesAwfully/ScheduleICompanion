import hashlib
import os
import secrets

from .database import connect, initialize
from .security import token_hash


def main() -> None:
    initialize()
    steam_id = 76561190000000000 + secrets.randbelow(9_999_999_999)
    career = "deployment-self-test"
    snapshot = secrets.token_bytes(64)
    digest = hashlib.sha256(snapshot).hexdigest().upper()
    token = "sic_test_" + secrets.token_urlsafe(24)
    with connect() as connection:
        connection.execute("INSERT INTO players (steam_id) VALUES (%s)", (steam_id,))
        connection.execute(
            "INSERT INTO api_tokens (token_hash, steam_id, label) VALUES (%s, %s, 'self-test')",
            (token_hash(token), steam_id),
        )
        connection.execute(
            """
            INSERT INTO backpack_revisions (steam_id, career_id, revision, content_hash, snapshot)
            VALUES (%s, %s, 1, %s, %s)
            """,
            (steam_id, career, digest, snapshot),
        )
        connection.execute(
            "INSERT INTO backpack_heads (steam_id, career_id, revision, content_hash) VALUES (%s, %s, 1, %s)",
            (steam_id, career, digest),
        )
        row = connection.execute(
            """
            SELECT r.snapshot FROM backpack_heads h
            JOIN backpack_revisions r USING (steam_id, career_id, revision)
            WHERE h.steam_id = %s AND h.career_id = %s
            """,
            (steam_id, career),
        ).fetchone()
        if row is None or bytes(row[0]) != snapshot:
            raise RuntimeError("database round-trip failed")
        connection.rollback()
    print("SELFTEST_OK")


if __name__ == "__main__":
    main()
