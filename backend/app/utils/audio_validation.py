"""
Audio validation utilities for voice input support.
"""
import os
from typing import Tuple


# Supported audio formats and their MIME types
SUPPORTED_AUDIO_FORMATS = {
    'audio/wav': ['.wav'],
    'audio/wave': ['.wav'],
    'audio/x-wav': ['.wav'],
    'audio/mpeg': ['.mp3'],
    'audio/mp3': ['.mp3'],
    'audio/ogg': ['.ogg'],
    'audio/flac': ['.flac'],
    'audio/x-flac': ['.flac']
}

MAX_AUDIO_SIZE = 10 * 1024 * 1024  # 10MB


def validate_audio(file) -> Tuple[bool, str]:
    """
    Validates uploaded audio file.
    
    Args:
        file: FileStorage object from Flask request
        
    Returns:
        Tuple of (is_valid, error_message)
    """
    # Check if file exists
    if not file:
        return False, "No audio file provided"
    
    # Check file type
    content_type = file.content_type
    if content_type not in SUPPORTED_AUDIO_FORMATS:
        supported = ', '.join(sorted(set(ext for exts in SUPPORTED_AUDIO_FORMATS.values() for ext in exts)))
        return False, f"Invalid audio format. Supported formats: {supported}"
    
    # Check file size
    file.seek(0, os.SEEK_END)
    size = file.tell()
    file.seek(0)
    
    if size > MAX_AUDIO_SIZE:
        return False, f"Audio file too large. Maximum {MAX_AUDIO_SIZE // (1024 * 1024)}MB"
    
    if size == 0:
        return False, "Audio file is empty"
    
    # Basic integrity check - try to read first few bytes
    try:
        # Read first 12 bytes to check file signature
        header = file.read(12)
        file.seek(0)
        
        if len(header) < 4:
            return False, "Audio file is corrupted or incomplete"
        
        # Check for common audio file signatures
        # WAV: RIFF....WAVE
        # MP3: ID3 or 0xFF 0xFB (frame sync)
        # OGG: OggS
        # FLAC: fLaC
        
        if content_type in ['audio/wav', 'audio/wave', 'audio/x-wav']:
            if not (header[:4] == b'RIFF' and header[8:12] == b'WAVE'):
                return False, "Invalid WAV file format"
        elif content_type in ['audio/mpeg', 'audio/mp3']:
            if not (header[:3] == b'ID3' or (header[0] == 0xFF and (header[1] & 0xE0) == 0xE0)):
                return False, "Invalid MP3 file format"
        elif content_type == 'audio/ogg':
            if not header[:4] == b'OggS':
                return False, "Invalid OGG file format"
        elif content_type in ['audio/flac', 'audio/x-flac']:
            if not header[:4] == b'fLaC':
                return False, "Invalid FLAC file format"
        
        return True, ""
        
    except Exception as e:
        file.seek(0)
        return False, f"Error validating audio file: {str(e)}"
