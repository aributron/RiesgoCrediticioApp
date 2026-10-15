from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field, field_validator

from credit_risk.data import ANOMALOUS_LATE_CODES

LateCount = Annotated[int, Field(ge=0, le=2147483647)]


class PredictRequest(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True, allow_inf_nan=False, hide_input_in_errors=True)

    ageYears: int = Field(ge=18, le=120)
    debtRatio: float = Field(ge=0)
    latePayments30To59DaysLast24Months: LateCount
    latePayments60To89DaysLast24Months: LateCount

    @field_validator("latePayments30To59DaysLast24Months", "latePayments60To89DaysLast24Months")
    @classmethod
    def reject_anomalous_codes(cls, value: int) -> int:
        if value in ANOMALOUS_LATE_CODES:
            raise ValueError("Los códigos 96/98 no son conteos admitidos por este baseline.")
        return value


class PredictResponse(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True, allow_inf_nan=False, hide_input_in_errors=True)

    defaultProbability: float = Field(ge=0, le=1)
    modelVersion: str = Field(min_length=1)
    predictionHorizonMonths: Literal[24]
    targetEvent: Literal["delinquency_90_days_or_more"]

