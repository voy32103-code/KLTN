"""
FastAPI AI Service — Core entry point.
Cung cấp 3 endpoints chính:
  - POST /api/chat     → Persona-based stakeholder response
  - POST /api/extract  → Requirement extraction từ conversation
  - POST /api/evaluate → Coverage evaluation (semantic matching)
"""
from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from dotenv import load_dotenv

load_dotenv()

app = FastAPI(
    title="ReqSimulator AI Service",
    description="Persona-driven stakeholder simulation & requirement analysis",
    version="0.1.0"
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["http://localhost:5173", "http://localhost:5000"],
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
