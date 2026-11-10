using UnityEngine;

public class FPSCounter : MonoBehaviour
{
    private float deltaTime;

    private void Update()
    {
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
        float fps = 1.0f / deltaTime;

        Debug.LogWarning($"[Performance Warning] Current Fps: {fps:F1}");
    }
}
