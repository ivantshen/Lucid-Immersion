"""
Tests for audio validation utilities.
"""
import pytest
import io
from werkzeug.datastructures import FileStorage
from app.utils.audio_validation import validate_audio, MAX_AUDIO_SIZE


class TestAudioValidation:
    """Tests for audio file validation."""
    
    def test_validate_audio_valid_wav(self):
        """Test that valid WAV file passes validation."""
        # Create a minimal valid WAV file
        wav_header = b'RIFF' + b'\x24\x00\x00\x00' + b'WAVE' + b'fmt ' + b'\x10\x00\x00\x00'
        wav_data = wav_header + b'\x00' * 100  # Add some data
        
        file = FileStorage(
            stream=io.BytesIO(wav_data),
            filename='test.wav',
            content_type='audio/wav'
        )
        
        is_valid, error = validate_audio(file)
        assert is_valid
        assert error == ""
    
    def test_validate_audio_valid_mp3(self):
        """Test that valid MP3 file passes validation."""
        # Create a minimal valid MP3 file with ID3 tag
        mp3_data = b'ID3' + b'\x03\x00\x00\x00\x00\x00\x00' + b'\xFF\xFB' + b'\x00' * 100
        
        file = FileStorage(
            stream=io.BytesIO(mp3_data),
            filename='test.mp3',
            content_type='audio/mpeg'
        )
        
        is_valid, error = validate_audio(file)
        assert is_valid
        assert error == ""
    
    def test_validate_audio_valid_ogg(self):
        """Test that valid OGG file passes validation."""
        # Create a minimal valid OGG file
        ogg_data = b'OggS' + b'\x00' * 100
        
        file = FileStorage(
            stream=io.BytesIO(ogg_data),
            filename='test.ogg',
            content_type='audio/ogg'
        )
        
        is_valid, error = validate_audio(file)
        assert is_valid
        assert error == ""
    
    def test_validate_audio_valid_flac(self):
        """Test that valid FLAC file passes validation."""
        # Create a minimal valid FLAC file
        flac_data = b'fLaC' + b'\x00' * 100
        
        file = FileStorage(
            stream=io.BytesIO(flac_data),
            filename='test.flac',
            content_type='audio/flac'
        )
        
        is_valid, error = validate_audio(file)
        assert is_valid
        assert error == ""
    
    def test_validate_audio_invalid_format(self):
        """Test that invalid audio format is rejected."""
        file = FileStorage(
            stream=io.BytesIO(b'some data'),
            filename='test.txt',
            content_type='text/plain'
        )
        
        is_valid, error = validate_audio(file)
        assert not is_valid
        assert 'Invalid audio format' in error
        assert '.wav' in error or '.mp3' in error
    
    def test_validate_audio_oversized_file(self):
        """Test that oversized audio file is rejected."""
        # Create a file larger than MAX_AUDIO_SIZE
        large_data = b'RIFF' + b'\x24\x00\x00\x00' + b'WAVE' + b'\x00' * (MAX_AUDIO_SIZE + 1000)
        
        file = FileStorage(
            stream=io.BytesIO(large_data),
            filename='large.wav',
            content_type='audio/wav'
        )
        
        is_valid, error = validate_audio(file)
        assert not is_valid
        assert 'too large' in error.lower()
        assert '10MB' in error
    
    def test_validate_audio_empty_file(self):
        """Test that empty audio file is rejected."""
        file = FileStorage(
            stream=io.BytesIO(b''),
            filename='empty.wav',
            content_type='audio/wav'
        )
        
        is_valid, error = validate_audio(file)
        assert not is_valid
        assert 'empty' in error.lower()
    
    def test_validate_audio_corrupted_wav(self):
        """Test that corrupted WAV file is rejected."""
        # Invalid WAV header (missing WAVE marker)
        corrupted_data = b'RIFF' + b'\x24\x00\x00\x00' + b'XXXX'
        
        file = FileStorage(
            stream=io.BytesIO(corrupted_data),
            filename='corrupted.wav',
            content_type='audio/wav'
        )
        
        is_valid, error = validate_audio(file)
        assert not is_valid
        assert 'Invalid WAV' in error
    
    def test_validate_audio_corrupted_mp3(self):
        """Test that corrupted MP3 file is rejected."""
        # Invalid MP3 data (no valid header)
        corrupted_data = b'XXX' + b'\x00' * 100
        
        file = FileStorage(
            stream=io.BytesIO(corrupted_data),
            filename='corrupted.mp3',
            content_type='audio/mpeg'
        )
        
        is_valid, error = validate_audio(file)
        assert not is_valid
        assert 'Invalid MP3' in error
    
    def test_validate_audio_corrupted_ogg(self):
        """Test that corrupted OGG file is rejected."""
        # Invalid OGG data
        corrupted_data = b'XXXX' + b'\x00' * 100
        
        file = FileStorage(
            stream=io.BytesIO(corrupted_data),
            filename='corrupted.ogg',
            content_type='audio/ogg'
        )
        
        is_valid, error = validate_audio(file)
        assert not is_valid
        assert 'Invalid OGG' in error
    
    def test_validate_audio_corrupted_flac(self):
        """Test that corrupted FLAC file is rejected."""
        # Invalid FLAC data
        corrupted_data = b'XXXX' + b'\x00' * 100
        
        file = FileStorage(
            stream=io.BytesIO(corrupted_data),
            filename='corrupted.flac',
            content_type='audio/flac'
        )
        
        is_valid, error = validate_audio(file)
        assert not is_valid
        assert 'Invalid FLAC' in error
    
    def test_validate_audio_no_file(self):
        """Test that missing file is rejected."""
        is_valid, error = validate_audio(None)
        assert not is_valid
        assert 'No audio file' in error
    
    def test_validate_audio_incomplete_file(self):
        """Test that incomplete audio file is rejected."""
        # File with less than 4 bytes
        incomplete_data = b'RI'
        
        file = FileStorage(
            stream=io.BytesIO(incomplete_data),
            filename='incomplete.wav',
            content_type='audio/wav'
        )
        
        is_valid, error = validate_audio(file)
        assert not is_valid
        assert 'corrupted' in error.lower() or 'incomplete' in error.lower()
