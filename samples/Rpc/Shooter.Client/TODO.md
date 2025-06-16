
 Problem: The RPC client was trying to create grain references before receiving the manifest from the server.

  Solution: Added a 1-second delay after starting the RPC client to allow time for:
  - The handshake to complete
  - The manifest to be received from the server
  - The client to update its grain type mappings

  Future Improvement: The RPC client should ideally expose an event or method to wait for readiness, such as:
  - WaitForHandshakeAsync()
  - IsReady property
  - HandshakeCompleted event

  This would eliminate the need for the arbitrary delay and make the connection process more robust.
