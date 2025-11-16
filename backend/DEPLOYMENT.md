# Deployment Guide - Google Cloud Run

## Prerequisites
- Google Cloud CLI installed ✅
- Google Cloud account with billing enabled
- Docker installed (optional, Cloud Run can build for you)

## Step 1: Set Up Google Cloud Project

```bash
# Login to Google Cloud
gcloud auth login

# Create a new project (or use existing)
gcloud projects create lucid-immersion-backend --name="Lucid Immersion Backend"

# Set the project
gcloud config set project lucid-immersion-backend

# Enable required APIs
gcloud services enable run.googleapis.com
gcloud services enable cloudbuild.googleapis.com
```

## Step 2: Set Environment Variables

You can use your existing `.env` file! Just pass the variables directly:

```bash
# Read from your existing .env file
source .env  # Mac/Linux
# or manually set them for the deployment command
```

Or create environment variables in the deployment command (see Step 3).

## Step 3: Deploy to Cloud Run

```bash
# Navigate to backend directory
cd backend

# Deploy (Cloud Run will build the Docker image for you)
# Option 1: Set env vars directly from your .env file
gcloud run deploy backend-api \
  --source . \
  --region us-central1 \
  --allow-unauthenticated \
  --set-env-vars GEMINI_API_KEY="your-key-here",API_KEY="your-key-here",FLASK_ENV="production",LOG_LEVEL="INFO" \
  --memory 1Gi \
  --cpu 1 \
  --timeout 60s \
  --max-instances 10

# Option 2: Use secrets (more secure for production)
# First create secrets:
# echo -n "your-gemini-key" | gcloud secrets create gemini-api-key --data-file=-
# echo -n "your-api-key" | gcloud secrets create api-key --data-file=-
# Then deploy with secrets:
# gcloud run deploy backend-api \
#   --source . \
#   --region us-central1 \
#   --set-secrets GEMINI_API_KEY=gemini-api-key:latest,API_KEY=api-key:latest

# Or if you want to use a specific region closer to you:
# --region us-east1 (Virginia)
# --region europe-west1 (Belgium)
# --region asia-northeast1 (Tokyo)
```

## Step 4: Get Your Service URL

After deployment, you'll see:
```
Service [backend-api] revision [backend-api-00001-abc] has been deployed and is serving 100 percent of traffic.
Service URL: https://backend-api-abc123-uc.a.run.app
```

## Step 5: Test Your Deployment

```bash
# Test health endpoint
curl https://backend-api-abc123-uc.a.run.app/health

# Should return:
# {"status":"healthy","timestamp":"2025-11-16T..."}
```

## Step 6: Update Unity

In your Unity script, update the endpoint URLs:

```csharp
string flaskEndpointUrl = "https://backend-api-abc123-uc.a.run.app/assist";
string askEndpointUrl = "https://backend-api-abc123-uc.a.run.app/ask";
string apiKey = "your-api-key-here"; // Same as API_KEY in .env.yaml
```

## Useful Commands

### View logs
```bash
gcloud run services logs read backend-api --region us-central1
```

### Update environment variables
```bash
gcloud run services update backend-api \
  --region us-central1 \
  --env-vars-file .env.yaml
```

### Redeploy after code changes
```bash
gcloud run deploy backend-api \
  --source . \
  --region us-central1
```

### Delete service (to stop billing)
```bash
gcloud run services delete backend-api --region us-central1
```

## Cost Estimate

Cloud Run pricing (as of 2024):
- **Free tier:** 2 million requests/month
- **After free tier:** ~$0.40 per million requests
- **Memory:** $0.0000025 per GB-second
- **CPU:** $0.00002400 per vCPU-second

**Typical cost for development:** $0-5/month

## Troubleshooting

### Build fails
- Check that all files in requirements.txt are available
- Ensure Dockerfile syntax is correct

### Service won't start
- Check logs: `gcloud run services logs read backend-api`
- Verify environment variables are set correctly

### 503 errors
- Check GEMINI_API_KEY is valid
- Increase memory/CPU if needed

### Context files not persisting
- Cloud Run is stateless - contexts will be lost on restart
- For production, use Cloud Storage or Firestore for persistence

## Security Notes

- The service is currently set to `--allow-unauthenticated` for easy testing
- For production, consider adding authentication
- Never commit `.env.yaml` to git
- Rotate API keys regularly
