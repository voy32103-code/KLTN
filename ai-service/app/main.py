from fastapi import FastAPI, Depends, Header, HTTPException, Request
from fastapi.middleware.cors import CORSMiddleware
from dotenv import load_dotenv
import os

load_dotenv()

# Dynamic CORS Configuration (SEC-08)
allowed_origins_raw = os.getenv("ALLOWED_CORS_ORIGINS", "http://localhost:5173,http://localhost:5000")
allowed_origins = [origin.strip() for origin in allowed_origins_raw.split(",") if origin.strip()]

# Service-to-Service API Key Authentication (SEC-09)
async def verify_api_key(request: Request, x_ai_service_key: str = Header(None, alias="X-AI-Service-Key")):
    if request.url.path == "/health":
        return
    if not x_ai_service_key:
        raise HTTPException(status_code=401, detail="Unauthorized: Missing X-AI-Service-Key header.")
    expected_key = os.getenv("AI_SERVICE_INTERNAL_KEY")
    if not expected_key:
        raise HTTPException(status_code=500, detail="AI Service Internal Key is not configured on the server.")
    if x_ai_service_key != expected_key:
        raise HTTPException(status_code=403, detail="Forbidden: Invalid Service API Key.")

app = FastAPI(
    title="ReqSimulator AI Service",
    description="Persona-driven stakeholder simulation & requirement analysis",
    version="0.1.0",
    dependencies=[Depends(verify_api_key)]
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=allowed_origins,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Import routers
from app.services.chat_service import router as chat_router
from app.services.extract_service import router as extract_router
from app.services.evaluate_service import router as evaluate_router

app.include_router(chat_router, prefix="/api")
app.include_router(extract_router, prefix="/api")
app.include_router(evaluate_router, prefix="/api")


@app.get("/health")
def health():
    return {"status": "ok", "service": "ai-service"}
