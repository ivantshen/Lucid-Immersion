# Voice Input Feature Specification

## Overview
Enable users to ask follow-up questions using voice input by holding the B button on Meta Quest 3 controllers. The audio is sent to the backend, transcribed to text using Google Cloud Speech-to-Text, and processed through the existing /ask endpoint.

## User Flow

```
User holds B button → Record audio → Release B button → Upload to /ask → 
Transcribe to text → Process with Gemini → Display answer
```

## Architecture

### Frontend (Unity - Meta Quest 3)
```
┌─────────────────────────────────────────┐
│         HeadlessConverter.cs            │
├─────────────────────────────────────────┤
│ 1. Detect B button press (OVRInput)    │
│ 2. Request microphone permission       │
│ 3. Start recording audio (Microphone)  │
│ 4. Show "Listening..." indicator       │
│ 5. Stop recording on button release    │
│ 6. Convert to WAV byte array           │
│ 7. Upload to /ask endpoint              │
│ 8. Display transcribed question         │
│ 9. Display answer steps                 │
└─────────────────────────────────────────┘
```

### Backend (Flask API)
```
┌─────────────────────────────────────────┐
│          /ask Endpoint                  │
├─────────────────────────────────────────┤
│ 1. Accept multipart/form-data           │
│ 2. Extract audio file (optional)        │
│ 3. Validate audio format & size         │
│ 4. Transcribe using Google Speech API   │
│ 5. Use transcribed text as question     │
│ 6. Load session context                 │
│ 7. Query Gemini with context            │
│ 8. Return answer steps                  │
└─────────────────────────────────────────┘
```

## API Changes

### Updated /ask Endpoint

**Request Format (Option 1 - Text only, existing):**
```json
POST /ask
Content-Type: application/json
Authorization: Bearer <API_KEY>

{
  "session_id": "abc-123",
  "question": "What size screwdriver do I need?"
}
```

**Request Format (Option 2 - Voice, new):**
```
POST /ask
Content-Type: multipart/form-data
Authorization: Bearer <API_KEY>

Fields:
- session_id: "abc-123"
- audio: <audio file> (WAV, MP3, OGG, or FLAC)
```

**Response Format (same for both):**
```json
{
  "status": "success",
  "session_id": "abc-123",
  "transcribed_question": "What size screwdriver do I need?",  // NEW: only if audio was provided
  "answer_steps": [
    "Use a Phillips head screwdriver, size #2",
    "This is the most common size for PC case screws"
  ],
  "context": {
    "task": "PSU_Install",
    "step": "1"
  }
}
```

## Implementation Tasks

### Backend Tasks (Task 8)

#### 8.1 Add Dependencies
```bash
# Add to requirements.txt
google-cloud-speech==2.21.0
```

#### 8.2 Audio Validation Utility
```python
# app/utils/audio_validation.py

def validate_audio(audio_file) -> tuple[bool, str]:
    """
    Validate audio file format and size.
    
    Returns:
        (is_valid, error_message)
    """
    # Check file extension
    # Check file size (max 10MB)
    # Verify audio file integrity
```

#### 8.3 Speech-to-Text Utility
```python
# app/utils/speech_to_text.py

def transcribe_audio(audio_bytes: bytes, audio_format: str) -> tuple[str, str]:
    """
    Transcribe audio to text using Google Cloud Speech-to-Text.
    
    Returns:
        (transcribed_text, error_message)
    """
    # Use Google Cloud Speech-to-Text API
    # Support WAV, MP3, OGG, FLAC formats
    # Handle API errors gracefully
```

#### 8.4 Update /ask Endpoint
```python
# app/routes/ask.py

@app.route('/ask', methods=['POST'])
def ask():
    # Check if request is JSON or multipart
    if request.is_json:
        # Existing text-only flow
        question = data.get('question')
    else:
        # New voice flow
        audio_file = request.files.get('audio')
        if audio_file:
            # Validate audio
            # Transcribe to text
            question = transcribed_text
    
    # Rest of the flow remains the same
```

### Unity Tasks (Task 9)

#### 9.1 Microphone Recording
```csharp
// HeadlessConverter.cs

private AudioClip recordedClip;
private bool isRecording = false;

void Update()
{
    // A button - take snapshot
    if (OVRInput.GetDown(OVRInput.Button.One))
    {
        TakeSnapshotAndUpload();
    }
    
    // B button - voice question
    if (OVRInput.GetDown(OVRInput.Button.Two))
    {
        StartRecording();
    }
    
    if (OVRInput.GetUp(OVRInput.Button.Two))
    {
        StopRecordingAndUpload();
    }
}

void StartRecording()
{
    // Request microphone permission
    // Start recording with Microphone.Start()
    // Show "Listening..." UI
}

void StopRecordingAndUpload()
{
    // Stop recording with Microphone.End()
    // Convert AudioClip to WAV byte array
    // Upload to /ask endpoint
}
```

#### 9.2 Audio Upload
```csharp
IEnumerator UploadAudioQuestion(byte[] audioData)
{
    WWWForm form = new WWWForm();
    form.AddBinaryData("audio", audioData, "question.wav", "audio/wav");
    form.AddField("session_id", sessionId);
    
    using (UnityWebRequest www = UnityWebRequest.Post(askEndpointUrl, form))
    {
        www.SetRequestHeader("Authorization", "Bearer " + apiKey);
        yield return www.SendWebRequest();
        
        if (www.result == UnityWebRequest.Result.Success)
        {
            AskResponse response = JsonUtility.FromJson<AskResponse>(www.downloadHandler.text);
            DisplayVoiceResponse(response);
        }
    }
}
```

#### 9.3 UI Updates
```csharp
void DisplayVoiceResponse(AskResponse response)
{
    // Header: Show transcribed question
    if (headerText != null)
    {
        headerText.text = $"You asked: {response.transcribed_question}";
    }
    
    // Subheader: Show timestamp
    if (subheaderText != null)
    {
        subheaderText.text = DateTime.Now.ToString("HH:mm:ss");
    }
    
    // Instructions: Show answer steps
    if (instructionText != null)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("Answer:");
        for (int i = 0; i < response.answer_steps.Length; i++)
        {
            sb.AppendLine($"{i + 1}. {response.answer_steps[i]}");
        }
        instructionText.text = sb.ToString();
    }
}
```

## Audio Format Specifications

### Recording Settings (Unity)
- **Format**: WAV (uncompressed)
- **Sample Rate**: 16000 Hz (optimal for speech recognition)
- **Channels**: Mono (1 channel)
- **Bit Depth**: 16-bit
- **Max Duration**: 30 seconds
- **Max File Size**: 10 MB

### Supported Formats (Backend)
- WAV (preferred)
- MP3
- OGG
- FLAC

## Error Handling

### Unity Errors
- **Microphone permission denied**: Show message "Please enable microphone access in settings"
- **Recording failed**: Show message "Failed to record audio. Please try again"
- **Upload failed**: Show message "Failed to send question. Check your connection"
- **No audio detected**: Show message "No audio detected. Please speak louder"

### Backend Errors
- **Invalid audio format**: Return 400 "Unsupported audio format"
- **Audio too large**: Return 413 "Audio file too large (max 10MB)"
- **Transcription failed**: Return 500 "Failed to transcribe audio"
- **Session not found**: Return 404 "Session not found"

## Testing Strategy

### Unit Tests
- Audio validation (format, size, integrity)
- Speech-to-text transcription (mocked API)
- /ask endpoint with audio input

### Integration Tests
- End-to-end voice question flow
- Backward compatibility with text-only requests
- Error handling for various failure scenarios

### Manual Testing
- Record audio on Meta Quest 3
- Test with different voice volumes
- Test with background noise
- Test with different accents/languages
- Test button press/release timing

## Security Considerations

1. **Audio File Validation**: Strictly validate file format and size
2. **Rate Limiting**: Limit number of voice requests per session
3. **Audio Storage**: Do NOT store audio files permanently (transcribe and discard)
4. **API Key Protection**: Ensure Google Cloud credentials are secure
5. **Content Filtering**: Consider filtering inappropriate content in transcriptions

## Performance Considerations

1. **Transcription Speed**: Google Speech-to-Text typically takes 1-3 seconds
2. **Audio Compression**: Consider compressing audio before upload to reduce bandwidth
3. **Caching**: Cache transcriptions for repeated audio (optional)
4. **Timeout**: Set 30-second timeout for transcription requests

## Future Enhancements

1. **Real-time Streaming**: Stream audio while recording for faster transcription
2. **Multi-language Support**: Detect and support multiple languages
3. **Voice Commands**: Add specific voice commands (e.g., "next step", "repeat")
4. **Noise Cancellation**: Improve audio quality with noise reduction
5. **Offline Mode**: Add offline speech recognition for basic commands

## Cost Estimation

### Google Cloud Speech-to-Text Pricing
- **Standard Model**: $0.006 per 15 seconds
- **Enhanced Model**: $0.009 per 15 seconds

**Example**: 
- 100 voice questions per day
- Average 5 seconds per question
- Cost: ~$0.20/day or $6/month (standard model)

Much cheaper than vision API calls!

## Success Metrics

1. **Transcription Accuracy**: >95% word accuracy
2. **Response Time**: <5 seconds from button release to answer display
3. **User Adoption**: % of users using voice vs text
4. **Error Rate**: <5% failed transcriptions
5. **User Satisfaction**: Positive feedback on voice feature
