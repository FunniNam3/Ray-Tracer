Shader "Custom/RayTracingShader"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // --- Settings and constants ---
			static const float PI = 3.1415;
            
            // Camera Settings
            float3 ViewParams;                  // View parameters for scaling
            float4x4 CamLocalToWorldMatrix;     // Camera transformation matrix
            float3 WorldSpaceCameraPos;         // Camera position in world space
            float2 Resolution;                  // Texture Resolution
            int FrameCount;                     // Current FrameCount
            int MaxBounceCount;                 // Max number of bounces per ray
            int NumRaysPerPixel;                // Number of rays per pixel

            // Enviornment Settings
            int EnableEnviornment;
            float4 SkyColorHorizon;
            float4 SkyColorZenith;
            float3 SunLightDirection;
            float SunFocus;
            float SunIntensity;
            float4 GroundColor;

            // Ray Struct
            struct Ray
            {
                float3 origin;                   // Start Location of Ray
                float3 dir;                     // Direction of Ray
            };

            struct RayTracingMaterial
            {
                float4 color;
                float4 emissionColor;
                float emissionStrength;
            };

            // Hit Info Struct
            struct HitInfo
            {
                bool didHit;                    // Did the ray hit?
                float dst;                      // distance the ray had to travel
                float3 hitPoint;                // The point that the ray hit the object
                float3 normal;                  // The normal of the object at that point
                RayTracingMaterial material;
            };

            struct Sphere
            {
                float3 position;                // Position of the sphere
                float radius;                   // Radius of the sphere
                RayTracingMaterial material;    // Spheres Material
            };

            HitInfo RaySphere(Ray ray, float3 sphereCenter, float sphereRadius)
            {
                HitInfo hitInfo = (HitInfo)0;
                float3 offsetRayOrigin = ray.origin - sphereCenter;
                // Using equation sqrLength(rayOrgin + rayDir * dst) = radius^2
                float a = dot(ray.dir, ray.dir);
                float b = 2 * dot(offsetRayOrigin, ray.dir);
                float c = dot(offsetRayOrigin, offsetRayOrigin) - (sphereRadius * sphereRadius);

                float discriminant = (b * b) - (4 * a * c);

                if (discriminant >= 0) {
                    float dst = -(b +  sqrt(discriminant)) / (2 * a);

                    if (dst >= 0) {
                        hitInfo.didHit = true;
                        hitInfo.dst = dst;
                        hitInfo.hitPoint = ray.origin + ray.dir * dst;
                        hitInfo.normal = normalize(hitInfo.hitPoint - sphereCenter);
                    }
                }

                return hitInfo;
            }

            StructuredBuffer<Sphere> Spheres;
            int NumSpheres;

            HitInfo CalculateRayCollision(Ray ray)
            {
                HitInfo closestHit = (HitInfo)0;
                // Closest hit is infinitely far away till we hit something
                closestHit.dst = 1.#INF;

                for (int i = 0; i < NumSpheres; i++){
                    Sphere sphere = Spheres[i];
                    HitInfo hitInfo = RaySphere(ray, sphere.position, sphere.radius);

                    if(hitInfo.didHit && hitInfo.dst < closestHit.dst){
                        closestHit = hitInfo;
                        closestHit.material = sphere.material;
                    }
                }

                return closestHit;
            }

            float RandomValue(inout uint state)
            {
                state = state * 747796405 + 2891336453;
                uint result = ((state >> ((state >> 28) + 4)) ^ state) * 277803737;
                result = (result >> 22) ^ result;
                return result / 4294967295.0;
            }

            float RandomValueNormalDist(inout uint state)
            {
                float t = 2 * 3.14159263 * RandomValue(state);
                float r = sqrt(-2 * log(RandomValue(state)));
                return r * cos(t);
            }

            float3 RandomDirection(inout uint state)
            {
                float x = RandomValueNormalDist(state);
                float y = RandomValueNormalDist(state);
                float z = RandomValueNormalDist(state);
                return normalize(float3(x,y,z));
            }

            float3 RandomHemiDir(float3 normal, inout uint rngState)
            {
                float3 dir = RandomDirection(rngState);
                return dir * sign(dot(normal, dir));
            }

            float3 GetEnviornmentLight(Ray ray)
            {
                float skyGradT = pow(smoothstep(0, 0.4, ray.dir.y), 0.35);
                float3 skyGrad = lerp(SkyColorHorizon, SkyColorZenith, skyGradT);
                float sun = pow(max(0, dot(ray.dir, -SunLightDirection)), SunFocus) * SunIntensity;

                float groundToSkyT = smoothstep(-0.01, 0, ray.dir.y);
                float sunMask = groundToSkyT >= 1;
                return lerp(GroundColor, skyGrad, groundToSkyT) + sun * sunMask;
            }

            float3 Trace(Ray ray, inout uint rngState)
            {
                float3 incomingLight = 0;
                float3 rayColor = 1;
                for(int i = 0; i <= MaxBounceCount; i++)
                {
                    HitInfo hitInfo = CalculateRayCollision(ray);
                    if(hitInfo.didHit)
                    {
                        ray.origin = hitInfo.hitPoint;
                        ray.dir = normalize(hitInfo.normal + RandomDirection(rngState));

                        RayTracingMaterial material = hitInfo.material;
                        float3 emittedLight = material.emissionColor * material.emissionStrength;
                        incomingLight += emittedLight * rayColor;
                        rayColor *= material.color;
                    }
                    else
                    {
                        if(EnableEnviornment)
                        {
                            incomingLight += GetEnviornmentLight(ray) * rayColor;
                        }
                        break;
                    }
                }

                return incomingLight;
            }

            float4 frag (v2f i) : SV_Target
            {
                // Create seed for random number generator
				uint2 numPixels = _ScreenParams.xy;
				uint2 pixelCoord = i.uv * numPixels;
				uint pixelIndex = pixelCoord.y * numPixels.x + pixelCoord.x;
				uint rngState = pixelIndex + FrameCount * 719393;
                
                // Create ray
                float3 viewPointLocal = float3(i.uv - 0.5, 1) * ViewParams;
                float3 viewPoint = mul(CamLocalToWorldMatrix, float4(viewPointLocal, 1)).xyz;

                Ray ray;
                ray.origin = WorldSpaceCameraPos;
                ray.dir = normalize(viewPoint - WorldSpaceCameraPos);

                // Calculate pixel color
                float3 totalIncomingLight = 0;

                for (int rayIndex = 0; rayIndex < NumRaysPerPixel; rayIndex++)
                {
                    totalIncomingLight += Trace(ray, rngState);
                }

                // Store view parameters as a color output (adjust based on needs)
                float3 pixelCol = totalIncomingLight / NumRaysPerPixel;
                return float4(pixelCol, 1);
            }
            
            ENDCG
        }
    }
}
