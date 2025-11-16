"""
Test script to send an image to the backend API.
Usage: python test_with_image.py
"""
import requests
import json
import base64
import os
from pathlib import Path

# Configuration
API_URL = "http://localhost:8080/assist"
API_KEY = os.getenv("API_KEY", "my-super-secret-key-12345")  # From .env
IMAGE_PATH = "test_images/ITB1.jpg"  # Update extension if needed (.png, .jpeg, etc.)

#Test data for Japanese homework scenario
TEST_DATA = {
    "task_step": "1",
    "current_task": "Japanese Homework",
    "gaze_vector": json.dumps({"x": 0.0, "y": 0.0, "z": 1.0}),
    "session_id": "japanese-homework-session"
}

# TEST_DATA = {
#     "task_step": "1",
#     "current_task": "Gym Workout",
#     "gaze_vector": json.dumps({"x": 0.0, "y": 0.0, "z": 1.0}),
#     "session_id": "gym-session"
# }

# TEST_DATA = {
#     "task_step": "1",
#     "current_task": "Burger",
#     "gaze_vector": json.dumps({"x": 0.0, "y": 0.0, "z": 1.0}),
#     "session_id": "restaurant-burger"
# }


def test_ask_followup(session_id: str, question: str):
    """
    Send a follow-up question to the /ask endpoint.
    
    Args:
        session_id: The session ID from the previous /assist call
        question: The follow-up question to ask
    
    Returns:
        The response JSON or None if failed
    """
    print("\n" + "=" * 60)
    print("Testing /ask Endpoint (Follow-up Question)")
    print("=" * 60)
    
    ask_url = "http://localhost:8080/ask"
    
    print(f"\n📤 Sending Follow-up Question:")
    print(f"   URL: {ask_url}")
    print(f"   Session ID: {session_id}")
    print(f"   Question: {question}")
    
    try:
        headers = {
            'Authorization': f'Bearer {API_KEY}',
            'Content-Type': 'application/json'
        }
        
        payload = {
            'session_id': session_id,
            'question': question
        }
        
        print(f"\n⏳ Processing...")
        response = requests.post(
            ask_url,
            json=payload,
            headers=headers,
            timeout=30
        )
        
        # Print response
        print(f"\n📥 Response:")
        print(f"   Status Code: {response.status_code}")
        
        if response.status_code == 200:
            print(f"   ✅ Success!")
            result = response.json()
            
            print(f"\n📋 Result:")
            print(f"   Session ID: {result.get('session_id', 'N/A')}")
            
            # Display answer steps
            answer_steps = result.get('answer_steps', [])
            if isinstance(answer_steps, list) and len(answer_steps) > 0:
                print(f"\n   💬 Answer:")
                for i, step in enumerate(answer_steps, 1):
                    print(f"      {i}. {step}")
            else:
                print(f"\n   💬 Answer: {answer_steps}")
            
            # Display context
            context = result.get('context', {})
            if context:
                print(f"\n   📝 Context:")
                print(f"      Task: {context.get('task', 'N/A')}")
                print(f"      Step: {context.get('step', 'N/A')}")
            
            # Pretty print full response
            print(f"\n📄 Full Response JSON:")
            print(json.dumps(result, indent=2))
            
            return result
            
        else:
            print(f"   ❌ Error!")
            try:
                error_data = response.json()
                print(f"\n   Error Details:")
                print(json.dumps(error_data, indent=2))
            except:
                print(f"\n   Response Text: {response.text}")
            return None
        
    except requests.exceptions.ConnectionError:
        print(f"\n❌ Connection Error!")
        print(f"   Make sure the Flask server is running")
        return None
        
    except requests.exceptions.Timeout:
        print(f"\n❌ Request Timeout!")
        print(f"   The server took too long to respond (> 30s)")
        return None
        
    except Exception as e:
        print(f"\n❌ Unexpected Error: {str(e)}")
        return None


def test_with_image(image_path: str):
    """
    Send an image to the backend API and print the response.
    
    Args:
        image_path: Path to the image file
        
    Returns:
        The session_id if successful, None otherwise
    """
    print("=" * 60)
    print("Testing /assist Endpoint (Initial Request)")
    print("=" * 60)
    
    # Check if image exists
    if not os.path.exists(image_path):
        print(f"❌ Error: Image not found at {image_path}")
        print(f"\nPlease ensure your image file exists at: {image_path}")
        print("Supported formats: .jpg, .jpeg, .png")
        return
    
    # Get file info
    file_size = os.path.getsize(image_path)
    file_ext = Path(image_path).suffix.lower()
    
    print(f"\n📁 Image Info:")
    print(f"   Path: {image_path}")
    print(f"   Size: {file_size:,} bytes ({file_size / 1024:.2f} KB)")
    print(f"   Format: {file_ext}")
    
    # Check file size
    if file_size > 5 * 1024 * 1024:
        print(f"⚠️  Warning: Image is larger than 5MB and will be rejected")
        return
    
    if file_size > 1 * 1024 * 1024:
        print(f"ℹ️  Note: Image will be compressed (> 1MB)")
    
    # Prepare request
    print(f"\n📤 Sending Request:")
    print(f"   URL: {API_URL}")
    print(f"   Task: {TEST_DATA['current_task']}")
    print(f"   Step: {TEST_DATA['task_step']}")
    
    try:
        # Open and send image
        with open(image_path, 'rb') as img_file:
            files = {'snapshot': (os.path.basename(image_path), img_file, f'image/jpeg')}
            headers = {'Authorization': f'Bearer {API_KEY}'}
            
            print(f"\n⏳ Processing...")
            response = requests.post(
                API_URL,
                files=files,
                data=TEST_DATA,
                headers=headers,
                timeout=30
            )
        
        # Print response
        print(f"\n📥 Response:")
        print(f"   Status Code: {response.status_code}")
        
        if response.status_code == 200:
            print(f"   ✅ Success!")
            result = response.json()
            
            print(f"\n📋 Result:")
            print(f"   Session ID: {result.get('session_id', 'N/A')}")
            print(f"   Instruction ID: {result.get('instruction_id', 'N/A')}")
            
            # Display instruction steps
            instruction_steps = result.get('instruction_steps', [])
            if isinstance(instruction_steps, list) and len(instruction_steps) > 0:
                print(f"\n   📝 Instructions:")
                for i, step in enumerate(instruction_steps, 1):
                    print(f"      {i}. {step}")
            else:
                print(f"\n   📝 Instruction: {instruction_steps}")
            
            print(f"\n   🎯 Target: {result.get('target_id', 'N/A')}")
            print(f"   📳 Haptic: {result.get('haptic_cue', 'N/A')}")
            
            # Pretty print full response
            print(f"\n📄 Full Response JSON:")
            print(json.dumps(result, indent=2))
            
            # Check if context was saved
            context_file = f"contexts/{result.get('session_id')}.json"
            if os.path.exists(context_file):
                print(f"\n💾 Context saved to: {context_file}")
            
            return result.get('session_id')
            
        else:
            print(f"   ❌ Error!")
            try:
                error_data = response.json()
                print(f"\n   Error Details:")
                print(json.dumps(error_data, indent=2))
            except:
                print(f"\n   Response Text: {response.text}")
        
    except requests.exceptions.ConnectionError:
        print(f"\n❌ Connection Error!")
        print(f"   Make sure the Flask server is running:")
        print(f"   cd backend && python app/main.py")
        return None
        
    except requests.exceptions.Timeout:
        print(f"\n❌ Request Timeout!")
        print(f"   The server took too long to respond (> 30s)")
        return None
        
    except Exception as e:
        print(f"\n❌ Unexpected Error: {str(e)}")
        return None
    
    print("\n" + "=" * 60)


def check_server():
    """Check if the Flask server is running."""
    try:
        response = requests.get("http://localhost:8080/health", timeout=5)
        if response.status_code == 200:
            print("✅ Server is running and healthy")
            return True
        else:
            print(f"⚠️  Server responded with status {response.status_code}")
            return False
    except:
        print("❌ Server is not running")
        print("\nTo start the server:")
        print("  cd backend")
        print("  python app/main.py")
        return False


if __name__ == "__main__":
    print("\n🚀 Backend API Test - /assist + /ask Flow\n")
    
    # Check if server is running
    if not check_server():
        print("\nPlease start the server first, then run this script again.")
        exit(1)
    
    print()
    
    # Step 1: Test with the image (initial /assist call)
    session_id = test_with_image(IMAGE_PATH)
    
    # Step 2: If successful, allow interactive follow-up questions
    if session_id:
        while True:
            # Ask if user wants to continue
            print("\n" + "=" * 60)
            continue_session = input("\n💬 Do you want to ask a follow-up question? (y/n): ").strip().lower()
            
            if continue_session not in ['y', 'yes']:
                print("\n👋 Ending session. Goodbye!")
                break
            
            # Get user's question
            question = input("\n❓ Enter your follow-up question: ").strip()
            
            if not question:
                print("⚠️  Question cannot be empty. Please try again.")
                continue
            
            # Send the question to /ask endpoint
            print()
            result = test_ask_followup(session_id, question)
            
            if not result:
                print("\n⚠️  Failed to get response. You can try again or exit.")
    else:
        print("\n⚠️  Skipping /ask test - no session_id from /assist call")
    
    print("\n✨ Test complete!\n")
