import os
from pathlib import Path

import psycopg


DATABASE_URL = os.environ["DATABASE_URL"]


def connect():
    return psycopg.connect(DATABASE_URL)


def initialize() -> None:
    schema = Path(__file__).with_name("schema.sql").read_text(encoding="utf-8")
    with connect() as connection:
        for statement in schema.split(";"):
            if statement.strip():
                connection.execute(statement)
        connection.commit()
