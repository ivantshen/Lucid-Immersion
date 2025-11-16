#!/bin/bash

# Deployment script for Google Cloud Run
# Usage: ./deploy.sh

set -e

echo "🚀 Deploying to Google Cloud Run..."

# Check if .env exists
if [ ! -f ".env" ]; then
    echo "❌ Error: .env not found!"
    echo "Please create .env file with your API keys."
    exit 1
fi

# Load environment variables from .env
export $(cat .env | grep -v '^#' | xargs)

# Check if gcloud is installed
if ! command -v gcloud &> /dev/null; then
    echo "❌ Error: gcloud CLI not found!"
    echo "Please install it from: https://cloud.google.com/sdk/docs/install"
    exit 1
fi

# Get current project
PROJECT=$(gcloud config get-value project 2>/dev/null)
if [ -z "$PROJECT" ]; then
    echo "❌ Error: No Google Cloud project set!"
    echo "Run: gcloud config set project YOUR_PROJECT_ID"
    exit 1
fi

echo "📦 Project: $PROJECT"
echo "🌍 Region: us-central1"
echo ""

# Deploy
echo "⏳ Deploying backend-api..."
gcloud run deploy backend-api \
  --source . \
  --region us-central1 \
  --allow-unauthenticated \
  --set-env-vars "GEMINI_API_KEY=${GEMINI_API_KEY},API_KEY=${API_KEY},FLASK_ENV=production,LOG_LEVEL=INFO" \
  --memory 1Gi \
  --cpu 1 \
  --timeout 60s \
  --max-instances 10 \
  --quiet

echo ""
echo "✅ Deployment complete!"
echo ""
echo "🔗 Service URL:"
gcloud run services describe backend-api --region us-central1 --format='value(status.url)'
echo ""
echo "📝 Test your deployment:"
echo "curl \$(gcloud run services describe backend-api --region us-central1 --format='value(status.url)')/health"
