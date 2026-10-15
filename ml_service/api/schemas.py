from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field

LateCount = Annotated[int, Field(ge=0, le=24)]


class PredictRequest(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True, allow_inf_nan=False, hide_input_in_errors=True)

    ageYears: int = Field(ge=18, le=120)
    debtRatio: float = Field(ge=0)
    latePayments30To59DaysLast24Months: LateCount
    latePayments60To89DaysLast24Months: LateCount

class PredictResponse(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True, allow_inf_nan=False, hide_input_in_errors=True)

    defaultProbability: float = Field(ge=0, le=1)
    modelVersion: str = Field(min_length=1)
    predictionHorizonMonths: Literal[24]
    targetEvent: Literal["delinquency_90_days_or_more"]

