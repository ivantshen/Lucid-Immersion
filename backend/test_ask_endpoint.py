"""
Test script to verify /ask endpoint accepts multipart form data
"""
import requests
import io

# Configuration
BASE_URL = "http://localhost:8080"  # Test local server
# BASE_URL = "https://backend-api-141904499148.us-central1.run.app"  # Production
API_KEY = "my-super-secret-key-12345"

def test_ask_with_audio():
    """Test /ask endpoint with audio file"""
    
    # Create a dummy WAV file (minimal valid WAV header)
    wav_header = b'RIFF' + b'\x00\x00\x00\x00' + b'WAVE' + b'fmt ' + b'\x10\x00\x00\x00'
    wav_data = wav_header + b'\x00' * 100  # Minimal WAV file
    
    # Prepare multipart form data
    files = {
        'audio': ('test.wav', io.BytesIO(wav_data), 'audio/wav')
    }
    
    data = {
        'session_id': 'test-session-123'
    }
    
    headers = {
        'Authorization': f'Bearer {API_KEY}'
    }
    
    print("Testing /ask endpoint with audio...")
    print(f"URL: {BASE_URL}/ask")
    print(f"Session ID: {data['session_id']}")
    
    try:
        response = requests.post(
            f"{BASE_URL}/ask",
            files=files,
            data=data,
            headers=headers,
            timeout=30
        )
        
        print(f"\nResponse Status: {response.status_code}")
        print(f"Response Headers: {dict(response.headers)}")
        print(f"Response Body: {response.text}")
        
        if response.status_code == 200:
            print("\n✅ SUCCESS: Endpoint accepts multipart form data!")
        else:
            print(f"\n❌ FAILED: Got status {response.status_code}")
            
    except Exception as e:
        print(f"\n❌ ERROR: {e}")

def test_ask_with_text():
    """Test /ask endpoint with JSON (backward compatibility)"""
    
    data = {
        'session_id': 'test-session-123',
        'question': 'What should I do next?'
    }
    
    headers = {
        'Authorization': f'Bearer {API_KEY}',
        'Content-Type': 'application/json'
    }
    
    print("\n\nTesting /ask endpoint with JSON...")
    print(f"URL: {BASE_URL}/ask")
    
    try:
        response = requests.post(
            f"{BASE_URL}/ask",
            json=data,
            headers=headers,
            timeout=30
        )
        
        print(f"\nResponse Status: {response.status_code}")
        print(f"Response Body: {response.text}")
        
        if response.status_code in [200, 404]:  # 404 is ok (session not found)
            print("\n✅ SUCCESS: Endpoint accepts JSON!")
        else:
            print(f"\n❌ FAILED: Got status {response.status_code}")
            
    except Exception as e:
        print(f"\n❌ ERROR: {e}")

if __name__ == "__main__":
    print("=" * 60)
    print("Testing /ask endpoint")
    print("=" * 60)
    
    test_ask_with_audio()
    test_ask_with_text()
    
    print("\n" + "=" * 60)
    print("Tests complete!")
    print("=" * 60)
