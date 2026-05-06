# Multi-stage Dockerfile for Resolution API
FROM python:3.11-slim AS base

# Set working directory
WORKDIR /app

# Install system dependencies
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    && rm -rf /var/lib/apt/lists/*

# Copy Python source tree (agents, shared, workflow, resolution_api)
COPY src/python /app

# Install Python dependencies
RUN pip install --no-cache-dir -r /app/resolution_api/requirements.txt

# Expose port (Azure Container Apps will inject PORT env var)
EXPOSE 8000

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8000/health || exit 1

# Set environment variables
ENV PYTHONUNBUFFERED=1
ENV PORT=8000

# Run the FastAPI app via uvicorn
CMD ["python", "-m", "uvicorn", "resolution_api.main:app", "--host", "0.0.0.0", "--port", "8000"]
