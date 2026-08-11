import os
import secrets

from dotenv import load_dotenv
from fastapi import Depends, FastAPI, Header, HTTPException, Request
from fastapi.middleware.cors import CORSMiddleware

load_dotenv()

allowed_origins_raw = os.getenv(
    "ALLOWED_CORS_ORIGINS",
    "http://localhost:5173,http://localhost:5000",
)
allowed_origins = [
    origin.strip()
    for origin in allowed_origins_raw.split(",")
    if origin.strip()
]

internal_service_key = os.getenv("AI_SERVICE_INTERNAL_KEY", "").strip()
if (
    len(internal_service_key) < 32
    or "change_me" in internal_service_key.lower()
    or "your_" in internal_service_key.lower()
):
    raise RuntimeError(
        "AI_SERVICE_INTERNAL_KEY must be configured with at least 32 non-placeholder characters."
    )


async def verify_api_key(
    request: Request,
    x_ai_service_key: str | None = Header(None, alias="X-AI-Service-Key"),
) -> None:
    if request.url.path in {"/", "/health"}:
        return
    if not x_ai_service_key:
        raise HTTPException(status_code=401, detail="Unauthorized.")
    if not secrets.compare_digest(x_ai_service_key, internal_service_key):
        raise HTTPException(status_code=403, detail="Forbidden.")


app = FastAPI(
    title="ReqSimulator AI Service",
    description="Persona-driven stakeholder simulation & requirement analysis",
    version="0.1.0",
    dependencies=[Depends(verify_api_key)],
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=allowed_origins,
    allow_methods=["GET", "POST"],
    allow_headers=["Content-Type", "X-AI-Service-Key"],
)

from app.services.chat_service import router as chat_router
from app.services.evaluate_service import router as evaluate_router
from app.services.extract_service import router as extract_router

app.include_router(chat_router, prefix="/api")
app.include_router(extract_router, prefix="/api")
app.include_router(evaluate_router, prefix="/api")


@app.get("/")
@app.head("/")
async def root():
    return {
        "status": "healthy",
        "service": "ReqSimulator AI Service",
    }


@app.get("/health")
@app.head("/health")
def health():
    return {"status": "ok", "service": "ai-service"}
