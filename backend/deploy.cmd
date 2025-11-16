@echo off
REM Deployment script for Google Cloud Run (Windows)
REM Usage: deploy.cmd

echo.
echo 🚀 Deploying to Google Cloud Run...
echo.

REM Check if .env exists
if not exist ".env" (
    echo ❌ Error: .env not found!
    echo Please create .env file with your API keys.
    exit /b 1
)

REM Load environment variables from .env
for /f "usebackq tokens=1,2 delims==" %%a in (".env") do (
    if not "%%a"=="" if not "%%a:~0,1%"=="#" set %%a=%%b
)

REM Check if gcloud is installed
where gcloud >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo ❌ Error: gcloud CLI not found!
    echo Please install it from: https://cloud.google.com/sdk/docs/install
    exit /b 1
)

REM Get current project
for /f "tokens=*" %%i in ('gcloud config get-value project 2^>nul') do set PROJECT=%%i
if "%PROJECT%"=="" (
    echo ❌ Error: No Google Cloud project set!
    echo Run: gcloud config set project YOUR_PROJECT_ID
    exit /b 1
)

echo 📦 Project: %PROJECT%
echo 🌍 Region: us-central1
echo.

REM Deploy
echo ⏳ Deploying backend-api...
gcloud run deploy backend-api ^
  --source . ^
  --region us-central1 ^
  --allow-unauthenticated ^
  --set-env-vars "GEMINI_API_KEY=%GEMINI_API_KEY%,API_KEY=%API_KEY%,FLASK_ENV=production,LOG_LEVEL=INFO" ^
  --memory 1Gi ^
  --cpu 1 ^
  --timeout 60s ^
  --max-instances 10 ^
  --quiet

echo.
echo ✅ Deployment complete!
echo.
echo 🔗 Service URL:
gcloud run services describe backend-api --region us-central1 --format="value(status.url)"
echo.
echo 📝 Test your deployment:
echo curl [SERVICE_URL]/health
