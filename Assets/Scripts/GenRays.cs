using UnityEngine;

public class GenRays : MonoBehaviour
{
    [Min(1)] public int pixelHeight = 7;
    [Min(1)] public int pixelWidth = 9;
    public float sphereScale = 0.1f;
    public bool ShaderOn = false;

    private Ray[] Rays;
    private GameObject[] Spheres;
    private Camera cam;

    // Cached previous values for optimization
    private int previousPixelHeight, previousPixelWidth;
    private float previousCamFOV;
    private Vector3 previousCamPosition;
    private Quaternion previousCamRotation;

    void Start()
    {
        cam = Camera.main;

        // Check if the camera is null
        if (cam == null)
        {
            Debug.LogError("No Main Camera found! Please ensure the camera is tagged as 'MainCamera'.");
            return; // Exit to prevent further null references
        }

        previousPixelHeight = pixelHeight;
        previousPixelWidth = pixelWidth;
        previousCamPosition = cam.transform.position;
        previousCamRotation = cam.transform.rotation;
        previousCamFOV = cam.fieldOfView;
        GenerateSpheres();
    }

    void Update()
    {
        if (
            pixelHeight != previousPixelHeight ||
        pixelWidth != previousPixelWidth ||
        cam.transform.position != previousCamPosition ||
        cam.transform.rotation != previousCamRotation ||
        cam.fieldOfView != previousCamFOV
        )
        {
            ResizeSpheres();
            PositionSpheres();

            previousPixelHeight = pixelHeight;
            previousPixelWidth = pixelWidth;
            previousCamPosition = cam.transform.position;
            previousCamRotation = cam.transform.rotation;
            previousCamFOV = cam.fieldOfView;
        }
    }

    void GenerateSpheres()
    {
        Spheres = new GameObject[pixelHeight * pixelWidth];
        Rays = new Ray[pixelHeight * pixelWidth];
        for (int i = 0; i < Spheres.Length; i++)
        {
            if (i < Spheres.Length)
            {
                Spheres[i] = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Spheres[i].transform.localScale = Vector3.one * sphereScale;
            }
        }
        PositionSpheres();
    }

    void ResizeSpheres()
    {
        GameObject[] tempSpheres = new GameObject[pixelHeight * pixelWidth];
        Ray[] tempRays = new Ray[pixelHeight * pixelWidth];

        for (int i = 0; i < tempSpheres.Length; i++)
        {
            if (i < Spheres.Length)
            {
                tempSpheres[i] = Spheres[i];
                tempRays[i] = Rays[i];
            }
            else
            {
                tempSpheres[i] = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                tempSpheres[i].transform.localScale = Vector3.one * sphereScale;
            }
        }

        for (int i = tempSpheres.Length; i < Spheres.Length; i++)
        {
            Destroy(Spheres[i]);
        }

        Spheres = tempSpheres;
        Rays = tempRays;
    }

    void PositionSpheres()
    {
        float height = 2 * cam.nearClipPlane * Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad / 2);
        float width = height * cam.aspect;
        Vector3 camPos = cam.transform.position;
        float startX = -width / 2;
        float startY = -height / 2;

        for (int row = 0; row < pixelHeight; row++)
        {
            for (int col = 0; col < pixelWidth; col++)
            {
                int index = row * pixelWidth + col;
                if (Spheres[index] == null) continue; // Skip if sphere is null

                Spheres[index].transform.localScale = Vector3.one * sphereScale;

                float x = startX + col * (width / (pixelWidth - 1));
                float y = startY + row * (height / (pixelHeight - 1));
                Spheres[index].transform.position = camPos + (cam.transform.rotation * new Vector3(x, y, cam.nearClipPlane));

                Vector3 spherePosition = Spheres[index].transform.position;
                Rays[index] = new Ray(camPos, (spherePosition - camPos).normalized);
            }
        }
    }
}