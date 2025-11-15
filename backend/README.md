Project Mentor: Technical Design Document
Version: 1.2
Date: November 15, 2025

1. Project Overview

Project Mentor is a real-time, headset-based coaching solution powered by Contextual AI. It is designed to run on the Meta Quest 3, using AR passthrough to provide context-aware, step-by-step guidance. When a user is stuck on a complex task, they can press a "Help" button, which captures their field of view. A multimodal AI analyzes the scene, queries a knowledge base, and presents instructional guidance in AR, supplemented by haptic feedback.

Hardware: Meta Quest 3

Game Engine: Unity 2022.3 (or newer)

Core SDKs: Meta XR Core SDK, Meta Interaction SDK (ISDK), Unity Sentis, Afference Haptics SDK

2. 🧠 Backend Architecture

The backend is a high-performance AI system designed to provide complex, procedural guidance. The primary objective is the Tier 2: Cloud "Reasoning" AI. The Tier 1 on-device system is an optional, future optimization for handling simple, immediate tasks.


2.1. Primary Backend: Tier 2 Cloud "Reasoning" AI (Serverless Container + RAG)

This is the main "brain" for the system, responsible for all complex, procedural guidance.

Hosting: Google Cloud Run. This is chosen over AWS Lambda/Cloud Functions because it allows us to package a full container with all Python dependencies (e.g., transformers, pinecone-client, opencv-python) and avoids complex layer management. It scales to zero (cost-effective) and has excellent cold-start performance.

API: A simple REST API (built with FastAPI in Python) with one primary endpoint: /assist.

Knowledge Base: Retrieval-Augmented Generation (RAG) System

Data Ingestion (Offline): All manuals (.pdf), schematics, and procedures are "chunked" into semantic paragraphs, each with rich metadata (e.g., {"source": "R740_Manual.pdf", "page": 42, "step": 5, "task": "PSU_Install"}).

Vector Database: Pinecone. Chosen for its serverless nature and extremely low p99 query latency, which is critical for our real-time use case. We will use a hybrid search (sparse/dense vectors) to get the best results.

Process Flow (The /assist Endpoint):

Receive Request: Cloud Run receives a POST request with the "Context Package" (see Section 4).

Analyze Context: A multimodal AI (Gemini) analyzes the image, gaze data, and last known step.

Formulate Query: The AI generates an internal search query (e.g., "User on 'PSU_Install', just finished step 4, looking at PDU").

Retrieve (R): The system queries the Pinecone vector database with this query. Pinecone returns the most relevant "chunks" of the technical manual (e.g., the exact text for Step 5).

Augment (A): The AI constructs a new prompt, forcing it to use the retrieved text as its only source of truth.

Generate (G): The AI generates a simple, clear, step-by-step instruction based only on the retrieved manual data.

Respond: The Cloud Run instance returns a simple JSON object to the headset (e.g., {"step_text": "Locate the 8-pin PDU cable...", "target_id": "J_PWR_1", "haptic_cue": "guide_to_target"}).

3. 👓 Unity Frontend Architecture

The Unity application is built on a modular, event-driven architecture to keep the main thread responsive and avoid freezing the display.

3.1. Core Scene Structure & SDKs

XR Rig: The standard Meta XR Rig prefab, configured for AR passthrough and hand tracking.

Interaction: Meta Interaction SDK (ISDK) will be used for all hand and controller interactions (poke, grab, ray). The "Help" button will be a PokeInteractable UI element.

Haptics: Afference Haptics SDK integrated into a central HapticManager service.

3.2. Key C# Scripts & Managers (High-Level)

ContextManager.cs (Singleton):

The central coordinator.

public void OnHelpButtonPressed(): This is the main trigger.

It assembles the "Context Package" by grabbing data from other managers:

CameraManager: Gets the passthrough "snapshot."

StateManager: Gets the current_task_step.

GazeManager: Gets the user's head/gaze vector.

It calls APIService.cs (Tier 2) to request assistance.

(Note: If Tier 1 is implemented, this script would first decide whether to call SentisService or APIService.)

APIService.cs (Singleton):

Handles all communication with the Tier 2 backend.

public void RequestAssistance(ContextPackage package): This method triggers the main asynchronous coroutine.

private IEnumerator PostAssistanceRequest(ContextPackage package):

Shows a "Thinking..." UI panel (UIManager.ShowLoading(true)).

Serializes the package: creates a WWWForm.

Adds the text data: form.AddField("task_step", package.taskStep).

Adds the image data: form.AddBinaryData("snapshot", package.imageBytes, "snapshot.jpg", "image/jpeg").

Uses UnityWebRequest to POST the form to the Cloud Run URL.

yield return www.SendWebRequest(); (This waits without freezing the app).

On response, it hides the loading panel and calls UIManager.SpawnHelpWindow(www.downloadHandler.text).

UIManager.cs (Singleton):

Manages all UI. Listens for events from other services.

public void SpawnHelpWindow(string jsonResponse):

Parses the JSON response using JsonUtility.FromJson<AIResponse>(jsonResponse).

Instantiates a "Help Window" prefab (a World-Space Canvas).

Populates the window with the step_text.

Uses the target_id to spawn a 3D arrow pointing to the correct location (using MRUK anchors if possible).

HapticManager.cs:

Listens for events (e.g., OnTargetApproach, OnStepComplete).

Calls the Afference SDK to play the corresponding haptic cues (guide_to_target, success_pulse).

4. 🔗 API & Data Model (Connecting Layer)

This defines the contract between the Unity Frontend and the Cloud Backend.

4.1. "Context Package" (Unity -> Backend)

Sent as multipart/form-data from Unity.

Field

Type (in form)

Description

snapshot

File (bytes)

A 1024x1024 JPG-compressed Texture2D. EncodeToJPG() of the passthrough.

task_step

String

The last completed step, e.g., "4".

current_task

String

The ID for the overall task, e.g., "PSU_Install".

gaze_vector

String

A JSON string of the user's head gaze vector (x, y, z).

api_key

String

A secure API key for authenticating the endpoint.

4.2. "AI Response" (Backend -> Unity)

Sent as a single JSON object from the backend.

Example JSON Response:

{
  "status": "success",
  "instruction_id": "a-123",
  "step_text": "Locate the 8-pin PDU cable (P/N 23-A). Plug it into the port labeled 'J_PWR_1'.",
  "target_id": "J_PWR_1",
  "haptic_cue": "guide_to_target",
  "next_step": {
    "instruction_id": "a-124",
    "step_text": "Secure the cable retention clip."
  }
}


This structure provides the frontend with everything it needs to display the current instruction, highlight the correct target, and trigger the right haptic feedback.

Here is a video that discusses how to connect a Unity application to a REST API to get JSON data, which is the core of the communication layer we've designed in Section 4. Unity REST API Tutorial This will be useful for implementing the APIService.cs script.