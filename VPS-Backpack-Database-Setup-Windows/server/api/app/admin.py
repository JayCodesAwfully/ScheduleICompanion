import argparse
import secrets

from .database import connect, initialize
from .security import token_hash


def create_token(steam_id: int, label: str) -> None:
    token = "sic_" + secrets.token_urlsafe(48)
    digest = token_hash(token)
    with connect() as connection:
        connection.execute("INSERT INTO players (steam_id) VALUES (%s) ON CONFLICT DO NOTHING", (steam_id,))
        connection.execute(
            "INSERT INTO api_tokens (token_hash, steam_id, label) VALUES (%s, %s, %s)",
            (digest, steam_id, label),
        )
        connection.commit()
    print(f"PLAYER_TOKEN={token}")


def revoke_all(steam_id: int) -> None:
    with connect() as connection:
        count = connection.execute(
            "UPDATE api_tokens SET revoked_at = now() WHERE steam_id = %s AND revoked_at IS NULL RETURNING token_hash",
            (steam_id,),
        ).fetchall()
        connection.commit()
    print(f"REVOKED={len(count)}")


def main() -> None:
    parser = argparse.ArgumentParser(description="Backpack API token administration")
    commands = parser.add_subparsers(dest="command", required=True)
    create = commands.add_parser("create-token")
    create.add_argument("steam_id", type=int)
    create.add_argument("label")
    revoke = commands.add_parser("revoke-all")
    revoke.add_argument("steam_id", type=int)
    args = parser.parse_args()
    initialize()
    if args.command == "create-token":
        create_token(args.steam_id, args.label)
    else:
        revoke_all(args.steam_id)


if __name__ == "__main__":
    main()
