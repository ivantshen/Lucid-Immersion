# Lucid Immersion - AR Hands-On Coach

**Hackathon Submission For Immerse The Bay 2025**

An AI-powered augmented reality coaching system for Meta Quest 3 that provides real-time, context-aware guidance for hands-on tasks like assembly, repair, and installation procedures.

---

## 🎯 Overview

Lucid Immersion is a Hands-On Coach that uses computer vision and AI to analyze what you see through your AR headset and provide step-by-step instructions. Whether you're building a PC, repairing equipment, or learning a new procedure, the system guides you through each step with spatial awareness and voice interaction.

### Key Features

- **Visual Analysis**: Press 'A' button to capture what you're looking at and get instant guidance
- **Voice Questions**: Hold 'B' button to ask follow-up questions about your current task
- **Spatial Awareness**: Instructions reference specific components based on your gaze direction
- **Context Retention**: System remembers your progress and previous instructions
- **Real-time Feedback**: Immediate responses optimized for hands-on work

---

## 🏗️ Architecture

### Frontend: Unity + Meta Quest 3
- **Platform**: Meta Quest 3 with AR passthrough
- **Engine**: Unity 2022.3+
- **SDKs**: Meta XR Core SDK, Meta Interaction SDK
- **Controls**: 
  - A Button (Right Controller): Capture snapshot and get guidance
  - B Button (Right Controller): Hold to record voice question

### Backend: Python + Google Cloud
- **Hosting**: Google Cloud Run (serverless containers)
- **Framework**: Flask with LangGraph workflow
- **AI Models**: 
  - Google Gemini 2.5 Flash (vision + text generation)
  - Google Cloud Speech-to-Text (voice transcription)
- **APIs**: 
  - `/assist` - Analyze image and provide step-by-step guidance
  - `/ask` - Answer follow-up questions with voice or text

---

## 🤖 AI Tools & Services

### 1. Google Gemini 2.5 Flash
- **Purpose**: Multimodal AI for image analysis and instruction generation
- **Usage**: 
  - Analyzes AR passthrough images
  - Generates context-aware step-by-step instructions
  - Answers follow-up questions
- **Configuration**: 
  - Temperature: 0.2 (for consistent, reliable guidance)
  - Max tokens: 4096 (output)
  - Response format: JSON

### 2. Google Cloud Speech-to-Text
- **Purpose**: Transcribe voice questions from users
- **Usage**: Converts audio recordings to text for the `/ask` endpoint
- **Configuration**:
  - Sample rate: 16000 Hz (matches Unity recording)
  - Language: en-US
  - Automatic punctuation enabled

### 3. LangGraph Workflow
- **Purpose**: Orchestrate multi-step AI processing
- **Workflow**:
  1. `analyze_and_instruct` - Process image and generate guidance
  2. `save_context` - Persist session data for follow-up questions

---

## 📋 Prerequisites

### Backend Requirements
- Python 3.9+
- Google Cloud account with:
  - Cloud Run enabled
  - Gemini API access
  - Speech-to-Text API enabled
- Service account credentials JSON file

### Frontend Requirements
- Unity 2022.3 or newer
- Meta Quest 3 headset
- Meta XR Core SDK
- Meta Interaction SDK

---

## 🚀 Setup Instructions

### Backend Setup

1. **Clone the repository**
   ```bash
   git clone <repository-url>
   cd Lucid-Immersion/backend
   ```

2. **Install Python dependencies**
   ```bash
   pip install -r requirements.txt
   ```

3. **Configure environment variables**
   
   Create a `.env` file in the `backend` directory:
   ```env
   GEMINI_API_KEY=your_gemini_api_key_here
   API_KEY=your_backend_api_key_here
   GOOGLE_APPLICATION_CREDENTIALS=path/to/service-account.json
   ```

4. **Set up Google Cloud credentials**
   - Download your service account JSON from Google Cloud Console
   - Place it in the `backend` directory
   - Update `GOOGLE_APPLICATION_CREDENTIALS` path in `.env`

5. **Run locally for testing**
   ```bash
   python app/main.py
   ```
   The server will start on `http://localhost:8080`

6. **Deploy to Google Cloud Run**
   ```bash
   gcloud run deploy backend-api \
     --source . \
     --region us-central1 \
     --allow-unauthenticated \
     --set-env-vars GEMINI_API_KEY=your_key,API_KEY=your_key
   ```

### Unity Setup

1. **Open the Unity project**
   - Open Unity Hub
   - Add project from `UnityStuff` directory
   - Ensure Unity 2022.3+ is installed

2. **Install Meta XR SDKs**
   - Open Package Manager
   - Install Meta XR Core SDK
   - Install Meta Interaction SDK

3. **Configure HeadlessConverter script**
   
   Open `Assets/HeadlessConverter.cs` and update:
   ```csharp
   public string assistEndpointUrl = "YOUR_CLOUD_RUN_URL/assist";
   public string askEndpointUrl = "YOUR_CLOUD_RUN_URL/ask";
   public string apiKey = "YOUR_BACKEND_API_KEY";
   ```

4. **Build and deploy to Quest 3**
   - Connect Meta Quest 3 via USB
   - Enable Developer Mode on headset
   - Build Settings → Android → Build and Run

---

## 🎮 How to Use

### Basic Workflow

1. **Put on Meta Quest 3 headset**
   - Enable AR passthrough mode
   - Launch the Lucid Immersion app

2. **Get visual guidance**
   - Look at what you're working on
   - Press **A button** on right controller
   - Wait 2-3 seconds for analysis
   - Read the step-by-step instructions displayed in AR

3. **Ask follow-up questions**
   - Press and hold **B button** on right controller
   - Speak your question (e.g., "Where is the power connector?")
   - Release **B button**
   - Wait for transcription and answer

4. **Continue your task**
   - Follow the instructions
   - Press **A** again when you need the next step
   - Use **B** anytime you have questions

### Example Session

```
User: [Presses A while looking at motherboard]
System: "Locate the 24-pin power connector on the right side of the motherboard.
         Align the connector with the socket, ensuring the clip faces outward.
         Press firmly until you hear a click.
         Verify the clip has locked into place."

User: [Holds B] "Where exactly is the 24-pin connector?"
System: "The 24-pin power connector is the large rectangular socket on the 
         right edge of the motherboard, usually labeled 'ATX_PWR' or 'JPWR1'.
         It's the largest power connector on the board.
         Look for a white or black plastic housing with 24 pins in two rows."
```

---

## 📁 Project Structure

```
Lucid-Immersion/
├── backend/                    # Python Flask backend
│   ├── app/
│   │   ├── main.py            # Flask application entry point
│   │   ├── routes/
│   │   │   ├── assist.py      # /assist endpoint (image analysis)
│   │   │   └── ask.py         # /ask endpoint (follow-up questions)
│   │   └── utils/
│   │       ├── speech_to_text.py      # Voice transcription
│   │       ├── audio_validation.py    # Audio file validation
│   │       └── image_processing.py    # Image compression
│   ├── llm.py                 # LangGraph workflow & prompts
│   ├── requirements.txt       # Python dependencies
│   └── README.md             # Backend technical documentation
│
├── UnityStuff/                # Unity project
│   └── Assets/
│       └── HeadlessConverter.cs   # Main Unity script
│
└── README.md                  # This file
```

---

## 🔧 Configuration

### Backend Configuration

**Environment Variables** (`.env` file):
- `GEMINI_API_KEY` - Your Google Gemini API key
- `API_KEY` - Backend authentication key (shared with Unity)
- `GOOGLE_APPLICATION_CREDENTIALS` - Path to service account JSON
- `CONTEXT_DIR` - Directory for session context files (default: `contexts`)

**Audio Settings**:
- Max file size: 10 MB
- Supported formats: WAV, MP3, OGG, FLAC
- Sample rate: 16000 Hz (must match Unity)

### Unity Configuration

**HeadlessConverter.cs Settings**:
- `assistEndpointUrl` - Backend /assist endpoint URL
- `askEndpointUrl` - Backend /ask endpoint URL
- `apiKey` - Must match backend API_KEY
- `currentTask` - Task identifier (e.g., "PC_Build")
- `taskStep` - Current step number
- `sendGazeData` - Enable/disable gaze tracking

**Audio Recording**:
- Sample rate: 16000 Hz
- Max recording time: 10 seconds
- Channels: Mono (automatically converted from stereo)

---

## 🐛 Troubleshooting

### Backend Issues

**"Audio transcription failed: Must use single channel"**
- Ensure Unity is recording at 16000 Hz sample rate
- Check that `SAMPLE_RATE` constant in HeadlessConverter.cs is 16000

**"GEMINI_API_KEY not configured"**
- Verify `.env` file exists in backend directory
- Check that environment variables are loaded
- For Cloud Run, verify environment variables are set in deployment

**"Session not found"**
- Press A button first to create a session before using B button
- Check that `contexts/` directory exists and is writable

### Unity Issues

**"No microphone found"**
- Grant microphone permissions in Quest 3 settings
- Check that `android.permission.RECORD_AUDIO` is requested

**"Camera is not playing"**
- Enable AR passthrough in Quest 3 settings
- Verify PassthroughCameraAccess component is assigned
- Check camera permissions

**"Server responded with error"**
- Verify backend URL is correct and accessible
- Check that API key matches between Unity and backend
- Look at backend logs for detailed error messages

---

## 📊 API Reference

### POST /assist
Analyze an image and provide step-by-step guidance.

**Request** (multipart/form-data):
- `snapshot` (file): JPEG image from AR headset
- `task_step` (string): Current step number
- `current_task` (string): Task identifier
- `gaze_vector` (string): JSON with x, y, z coordinates
- `session_id` (string, optional): Session identifier

**Response** (JSON):
```json
{
  "status": "success",
  "session_id": "uuid",
  "instruction_id": "uuid-step",
  "instruction_steps": [
    "First step",
    "Second step",
    "Third step"
  ],
  "target_id": "component_id",
  "haptic_cue": "guide_to_target",
  "image_analysis": "Brief description of what's visible",
  "timestamp": "2025-11-16T12:00:00Z"
}
```

### POST /ask
Answer a follow-up question about the current task.

**Request** (multipart/form-data):
- `session_id` (string): Session ID from /assist response
- `audio` (file, optional): WAV audio file with question
- `question` (string, optional): Text question (if no audio)

**Response** (JSON):
```json
{
  "status": "success",
  "session_id": "uuid",
  "answer_steps": [
    "First point",
    "Second point",
    "Third point"
  ],
  "transcribed_question": "User's question",
  "context": {
    "task": "PC_Build",
    "step": "4",
    "previous_instruction": "Previous steps..."
  }
}
```

---

## 🤝 Contributing

This is a hackathon project for Immerse The Bay 2025. Contributions, suggestions, and feedback are welcome!

---

## 🙏 Acknowledgments

- Meta for Quest 3 and XR SDKs
- Google Cloud for Gemini and Speech-to-Text APIs
- Immerse The Bay 2025 hackathon sponsors and organizers
