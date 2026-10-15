from contextlib import asynccontextmanager
from pathlib import Path

from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse

from api.predictor import ModelUnavailableError, PredictionError, Predictor
from api.schemas import PredictRequest, PredictResponse

ARTIFACTS = Path(__file__).resolve().parents[1] / "artifacts"


def create_app(artifacts: Path = ARTIFACTS) -> FastAPI:
    @asynccontextmanager
    async def lifespan(application: FastAPI):
        application.state.predictor = None
        application.state.model_error = None
        try:
            # Una sola carga por inicio del proceso, compartida por health y predict.
            application.state.predictor = Predictor.load(artifacts)
        except ModelUnavailableError as error:
            application.state.model_error = str(error)
        yield
        application.state.predictor = None

    application = FastAPI(
        title="Riesgo crediticio — API local PoC",
        version="1.0.0",
        description="Probabilidad estimada de morosidad grave a 24 meses. Sin decisiones automáticas.",
        lifespan=lifespan,
    )

    @application.exception_handler(RequestValidationError)
    async def invalid_request(_request: Request, error: RequestValidationError):
        # No registrar ni devolver input, ctx o cuerpo: pueden contener datos arbitrarios.
        details = [{
            "field": ".".join(str(part) for part in item["loc"]),
            "type": item["type"],
            "message": item["msg"],
        } for item in error.errors()]
        return JSONResponse(status_code=422, content={
            "error": {"code": "invalid_input", "message": "Las entradas no cumplen el contrato de predicción.", "details": details}
        })

    @application.get("/health", responses={503: {"description": "Modelo no disponible"}})
    def health(request: Request):
        predictor = request.app.state.predictor
        if predictor is None:
            return JSONResponse(status_code=503, content={
                "status": "unavailable", "modelAvailable": False,
                "message": request.app.state.model_error or "El modelo no está disponible.",
            })
        return {"status": "ok", "modelAvailable": True, "modelVersion": predictor.metadata.model_version}

    @application.post("/v1/predict", response_model=PredictResponse,
                      responses={422: {"description": "Entrada inválida"},
                                 503: {"description": "Modelo no disponible"},
                                 500: {"description": "Error de predicción"}})
    def predict(payload: PredictRequest, request: Request):
        predictor = request.app.state.predictor
        if predictor is None:
            return JSONResponse(status_code=503, content={
                "error": {"code": "model_unavailable",
                          "message": request.app.state.model_error or "El modelo no está disponible."}
            })
        try:
            return predictor.predict(payload)
        except PredictionError as error:
            return JSONResponse(status_code=500, content={
                "error": {"code": "prediction_failed", "message": str(error)}
            })

    return application


app = create_app()

