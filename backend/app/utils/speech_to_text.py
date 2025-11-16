"""
Speech-to-text utilities using Google Cloud Speech-to-Text API.
"""
from google.cloud import speech
from typing import Tuple
import io


def get_encoding_from_content_type(content_type: str) -> speech.RecognitionConfig.AudioEncoding:
    """
    Map content type to Google Speech API encoding.
    
    Args:
        content_type: MIME type of the audio file
        
    Returns:
        AudioEncoding enum value
    """
    encoding_map = {
        'audio/wav': speech.RecognitionConfig.AudioEncoding.LINEAR16,
        'audio/wave': speech.RecognitionConfig.AudioEncoding.LINEAR16,
        'audio/x-wav': speech.RecognitionConfig.AudioEncoding.LINEAR16,
        'audio/mpeg': speech.RecognitionConfig.AudioEncoding.MP3,
        'audio/mp3': speech.RecognitionConfig.AudioEncoding.MP3,
        'audio/ogg': speech.RecognitionConfig.AudioEncoding.OGG_OPUS,
        'audio/flac': speech.RecognitionConfig.AudioEncoding.FLAC,
        'audio/x-flac': speech.RecognitionConfig.AudioEncoding.FLAC,
    }
    
    return encoding_map.get(content_type, speech.RecognitionConfig.AudioEncoding.LINEAR16)


def transcribe_audio(audio_bytes: bytes, content_type: str, language_code: str = 'en-US') -> Tuple[bool, str, str]:
    """
    Transcribe audio to text using Google Cloud Speech-to-Text API.
    
    Args:
        audio_bytes: Audio file content as bytes
        content_type: MIME type of the audio file
        language_code: Language code for transcription (default: en-US)
        
    Returns:
        Tuple of (success, transcribed_text, error_message)
    """
    try:
        # Initialize the Speech client
        client = speech.SpeechClient()
        
        # Prepare the audio
        audio = speech.RecognitionAudio(content=audio_bytes)
        
        # Configure recognition
        # Let the API auto-detect channel count to support both mono and stereo
        config = speech.RecognitionConfig(
            encoding=get_encoding_from_content_type(content_type),
            language_code=language_code,
            enable_automatic_punctuation=True,
            model='default',
        )
        
        # Perform the transcription
        response = client.recognize(config=config, audio=audio)
        
        # Extract transcription from response
        if not response.results:
            return False, "", "No speech detected in audio"
        
        # Combine all transcription results
        transcription = ' '.join(
            result.alternatives[0].transcript
            for result in response.results
            if result.alternatives
        )
        
        if not transcription.strip():
            return False, "", "No speech detected in audio"
        
        return True, transcription.strip(), ""
        
    except Exception as e:
        error_msg = str(e)
        
        # Provide more user-friendly error messages
        if 'INVALID_ARGUMENT' in error_msg:
            return False, "", "Invalid audio format or corrupted file"
        elif 'UNAUTHENTICATED' in error_msg:
            return False, "", "Speech-to-Text API authentication failed"
        elif 'PERMISSION_DENIED' in error_msg:
            return False, "", "Speech-to-Text API access denied"
        elif 'RESOURCE_EXHAUSTED' in error_msg:
            return False, "", "Speech-to-Text API quota exceeded"
        else:
            return False, "", f"Transcription failed: {error_msg}"
