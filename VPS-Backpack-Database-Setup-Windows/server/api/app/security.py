import hashlib
import hmac
import os

from fastapi import Depends, HTTPException, status
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer

from .database import connect


TOKEN_PEPPER = os.environ["TOKEN_PEPPER"].encode("utf-8")
bearer = HTTPBearer(auto_error=False)


def token_hash(token: str) -> str:
    return hmac.new(TOKEN_PEPPER, token.encode("utf-8"), hashlib.sha256).hexdigest()


def authenticated_steam_id(
    credentials: HTTPAuthorizationCredentials | None = Depends(bearer),
) -> int:
    if credentials is None or credentials.scheme.lower() != "bearer" or len(credentials.credentials) < 32:
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="A valid bearer token is required")
    digest = token_hash(credentials.credentials)
    with connect() as connection:
        row = connection.execute(
            """
            UPDATE api_tokens SET last_used_at = now()
            WHERE token_hash = %s AND revoked_at IS NULL
            RETURNING steam_id
            """,
            (digest,),
        ).fetchone()
        connection.commit()
    if row is None:
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Token is invalid or revoked")
    return int(row[0])
