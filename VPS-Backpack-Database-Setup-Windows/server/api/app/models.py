from pydantic import BaseModel, Field, field_validator


class SnapshotPut(BaseModel):
    revision: int = Field(ge=1)
    contentHash: str
    transactionTailHash: str | None = None
    snapshotBase64: str = Field(min_length=4, max_length=7_500_000)

    @field_validator("contentHash", "transactionTailHash")
    @classmethod
    def valid_hash(cls, value: str | None):
        if value is not None and (len(value) != 64 or any(ch not in "0123456789abcdefABCDEF" for ch in value)):
            raise ValueError("hashes must be 64 hexadecimal characters")
        return value.upper() if value else value


class RestoreResult(BaseModel):
    revision: int
    contentHash: str
