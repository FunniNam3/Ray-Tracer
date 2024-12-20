Shader "RayTracingShader"
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
            #pragma multi_compile _ DEBUG_VIS

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
            float3 ViewParams;                      // View parameters for scaling
            float4x4 CamLocalToWorldMatrix;         // Camera transformation matrix
            int FrameCount;                         // Current FrameCount
            int MaxBounceCount;                     // Max number of bounces per ray
            int NumRaysPerPixel;                    // Number of rays per pixel
            float DefocusStrength;                  // Makes rays less focused
            float DivergeStrength;                  // Causes rays to diverge (Helps with Antialiasing)
            float FocusDistance;                    // Distance of Focus plane

            // Enviornment Settings
            int UseSky;
			float3 SunColor;
			float SunFocus = 500;
			float SunIntensity = 10;

            int visMode;
            float debugVisScale;

            // Ray Struct
            struct Ray
            {
                float3 origin;                      // Start Location of Ray
                float3 dir;                         // Direction of Ray
                float3 invDir;                       // Inverse Direction of Ray
            };
            
            // Triangle Struct
            struct Triangle
            {
                float3 posA, posB, posC;            // Triange Verticies
                float3 normalA, normalB, normalC;   // Triangle Normals at Verts
            };

            // Hit Info Struct
            struct TriangleHitInfo
            {
                bool didHit;                        // Did the ray hit?
                float dst;                          // distance the ray had to travel
                float3 hitPoint;                    // The point that the ray hit the object
                float3 normal;                      // The normal of the object at that point
                int triIndex;
            };

            struct RayTracingMaterial
            {
                int useTexture;
                int textureIndex;
                float4 color;
                float4 emissionColor;
                float emissionStrength;
                float4 specularColor;
                float specularProbability;
                float smoothness;
                float refractIndx;
                float transparency;
            };

            struct Model
            {
                int nodeOffset;
                int triOffset;
                float4x4 worldToLocalMatrix;
                float4x4 localToWorldMatrix;
                RayTracingMaterial material;
            };

            struct BVHNode
            {
                float3 boundsMin;
                float3 boundsMax;
                int startIndex;
                int triangleCount;
            };

            struct ModelHitInfo
            {
                bool didHit;
                float3 normal;
                float3 hitPoint;
                float dst;
                RayTracingMaterial material;
            };

            struct Light
            {
                float4 emissionColor;
                float emissionStrength;
                float3 modelPosition;
                int modelIndex;
            };

            // Buffers
            StructuredBuffer<Model> ModelInfo;
            StructuredBuffer<Triangle> Triangles;
            StructuredBuffer<BVHNode> Nodes;
            StructuredBuffer<Light> Lights;
            int triangleCount;
            int modelCount;
            int lightCount;

            // RNG Functions

            float RandomValue(inout uint state)
            {
                state = state * 747796405 + 2891336453;
                uint result = ((state >> ((state >> 28) + 4)) ^ state) * 277803737;
                result = (result >> 22) ^ result;
                return result / 4294967295.0;
            }

            float2 RandomPointInCircle(inout uint rngState)
            {
                float angle = RandomValue(rngState) * 2 * PI;
                float2 pointOnCircle = float2(cos(angle), sin(angle));
                return pointOnCircle * sqrt(RandomValue(rngState));
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

            // Get Light From Enviornment
            float3 GetEnvironmentLight(float3 dir)
			{
				if (UseSky == 0) return 0;
				const float3 GroundColor = float3(0.35, 0.3, 0.35);
				const float3 SkyColorHorizon = float3(1, 1, 1);
				const float3 SkyColorZenith = float3(0.08, 0.37, 0.73);

				float skyGradientT = pow(smoothstep(0, 0.4, dir.y), 0.35);
				float groundToSkyT = smoothstep(-0.01, 0, dir.y);
				float3 skyGradient = lerp(SkyColorHorizon, SkyColorZenith, skyGradientT);
				float sun = pow(max(0, dot(dir, _WorldSpaceLightPos0.xyz)), SunFocus) * SunIntensity;
				// Combine ground, sky, and sun
				float3 composite = lerp(GroundColor, skyGradient, groundToSkyT) + sun * SunColor * (groundToSkyT >= 1);
				return composite;
			}

            TriangleHitInfo RayTriangle(Ray ray, Triangle tri)
            {
                float3 edgeAB = tri.posB - tri.posA;
                float3 edgeAC = tri.posC - tri.posA;
                float3 normalVector = cross(edgeAB, edgeAC);
                float3 ao = ray.origin - tri.posA;
                float3 dao = cross(ao, ray.dir);

                float determinant = -dot(ray.dir, normalVector);
                float invDet = 1 / determinant;

                // Calculate dst to Triangle and barycentric coordinates of intersection point
                float dst = dot(ao, normalVector) * invDet;
                float u = dot(edgeAC, dao) * invDet;
                float v = -dot(edgeAB, dao) * invDet;
                float w = 1 - u - v;

                // init hit info
                TriangleHitInfo hitInfo;
                hitInfo.didHit = determinant >= 1E-6 && dst >= -1E-6 && u >= -1E-6 && v >= -1E-6 && w >= -1E-6;
                hitInfo.hitPoint = ray.origin + ray.dir * dst;
                hitInfo.normal = normalize(tri.normalA * w + tri.normalB * u + tri.normalC * v);
                hitInfo.dst = dst;
                return hitInfo;
            }

            float RayBoundingBoxDst(Ray ray, float3 boxMin, float3 boxMax)
            {
                float3 tMin = (boxMin - ray.origin) * ray.invDir;
                float3 tMax = (boxMax - ray.origin) * ray.invDir;
                float3 t1 = min(tMin, tMax);
                float3 t2 = max(tMin, tMax);
                float tNear = max(max(t1.x, t1.y), t1.z);
                float tFar = min(min(t2.x, t2.y), t2.z);

                bool hit = tFar >= tNear && tFar > 0;
                float dst = hit ? tNear > 0 ? tNear : 0 : 1.#INF;
                return dst;
            }

            TriangleHitInfo RayTriangleBVH(inout Ray ray, float rayLength, int nodeOffset, int triOffset, inout int2 stats)
            {
                TriangleHitInfo result;
                result.dst = rayLength;
                result.triIndex = -1;

                int stack[64];
                int stackIndex = 0;
                stack[stackIndex++] = nodeOffset + 0;

                while (stackIndex > 0)
                {
                    BVHNode node = Nodes[stack[--stackIndex]];
                    bool isLeaf = node.triangleCount > 0;

                    if(isLeaf)
                    {
                        for(int i = 0; i < node.triangleCount; i++)
                        {
                            Triangle tri = Triangles[triOffset + node.startIndex + i];
                            TriangleHitInfo triHitInfo = RayTriangle(ray, tri);
                            stats[0]++;

                            if (triHitInfo.didHit && triHitInfo.dst < result.dst)
                            {
                                result = triHitInfo;
                                result.triIndex = node.startIndex + i;
                            }
                        }
                    }
                    else
                    {
                        int childIndexA = nodeOffset + node.startIndex + 0;
                        int childIndexB = nodeOffset + node.startIndex + 1;
                        BVHNode childA = Nodes[childIndexA];
                        BVHNode childB = Nodes[childIndexB];

                        float dstA = RayBoundingBoxDst(ray, childA.boundsMin, childA.boundsMax);
                        float dstB = RayBoundingBoxDst(ray, childB.boundsMin, childB.boundsMax);
                        stats[1] += 2;

                        // Look at closeset child node first
                        bool isNearestA = dstA <= dstB;
                        float dstNear = isNearestA ? dstA : dstB;
                        float dstFar = isNearestA ? dstB : dstA;
                        int childIndexNear = isNearestA ? childIndexA : childIndexB;
                        int childIndexFar = isNearestA ? childIndexB : childIndexA;

                        if(dstFar < result.dst) stack[stackIndex++] = childIndexFar;
                        if(dstNear < result.dst) stack[stackIndex++] = childIndexNear;
                    }
                }

                return result;
            }

            ModelHitInfo CalculateRayCollision(Ray worldRay, out int2 stats)
            {
                ModelHitInfo result;
                result.dst = 1.#INF;
                Ray localRay;
                
                for(int i = 0; i < modelCount; i++)
                {
                    Model model = ModelInfo[i];
                    localRay.origin = mul(model.worldToLocalMatrix, float4(worldRay.origin, 1));
                    localRay.dir = mul(model.worldToLocalMatrix, float4(worldRay.dir, 0));
                    localRay.invDir = 1 / localRay.dir;

                    TriangleHitInfo hit = RayTriangleBVH(localRay, result.dst, model.nodeOffset, model.triOffset, stats);

                    if(hit.dst < result.dst)
                    {
                        result.didHit = true;
                        result.dst = hit.dst;
                        result.normal = normalize(mul(model.localToWorldMatrix, float4(hit.normal, 0)));
                        result.hitPoint = worldRay.origin + worldRay.dir * hit.dst;
                        result.material = model.material;
                    }
                }

                return result;
            }

            ModelHitInfo CalculateLightCollision(Ray worldRay, int modelIndex)
            {
                ModelHitInfo result;
                result.dst = 1.#INF;
                Ray localRay;
                int2 stats;
                
                for(int i = 0; i < modelCount; i++)
                {
                    if(modelIndex == i)
                    {
                        continue;
                    }
                    Model model = ModelInfo[i];
                    localRay.origin = mul(model.worldToLocalMatrix, float4(worldRay.origin, 1));
                    localRay.dir = mul(model.worldToLocalMatrix, float4(worldRay.dir, 0));
                    localRay.invDir = 1 / localRay.dir;

                    TriangleHitInfo hit = RayTriangleBVH(localRay, result.dst, model.nodeOffset, model.triOffset, stats);

                    if(hit.dst < result.dst)
                    {
                        result.didHit = true;
                        result.dst = hit.dst;
                        result.normal = normalize(mul(model.localToWorldMatrix, float4(hit.normal, 0)));
                        result.hitPoint = worldRay.origin + worldRay.dir * hit.dst;
                        result.material = model.material;
                    }
                }

                return result;
            }

            float2 mod2(float2 x, float2 y)
            {
                return x - y * floor(x / y);
            }

            float reflectance(float cosine, float refraction_Index)
            {
                // Using Schlick's approximation for reflectance.
                float r0 = pow((1 - refraction_Index) / (1 + refraction_Index), 2);
                return r0 + (1 - r0) * pow((1 - cosine), 5);
            }

            float3 SampleLightSource(inout uint rngState, out float pdf, out int Index)
            {
                int lightIndex = (int)(RandomValue(rngState) * lightCount);
                Index = Lights[lightIndex].modelIndex;
                Model model = ModelInfo[Index];
                BVHNode node = Nodes[model.nodeOffset];
                
                float3 center = mul(model.localToWorldMatrix, float4((node.boundsMax + node.boundsMin)/2,1));

                pdf = 1.0 / lightCount;

                return center;
            }

            float3 Trace(float3 rayOrigin, float3 rayDir, inout uint rngState)
            {
                float3 incomingLight = 0;
                float3 rayColor = 1;

                int2 stats;
                float dstSum = 0;

                for(int bounceIndex = 0; bounceIndex <= MaxBounceCount; bounceIndex++)
                {
                    Ray ray;
                    ray.origin = rayOrigin;
                    ray.dir = rayDir;
                    ModelHitInfo hitInfo = CalculateRayCollision(ray, stats);

                    if(hitInfo.didHit)
                    {
                        dstSum += hitInfo.dst;
                        RayTracingMaterial material = hitInfo.material;

                        if (RandomValue(rngState) * MaxBounceCount < bounceIndex) 
                        {
                            // Sample light source
                            int modelIndex;
                            float lightPdf;
                            float3 lightPos = SampleLightSource(rngState, lightPdf, modelIndex);
                            float3 lightDir = normalize(lightPos - hitInfo.hitPoint);
                            float lightDist = length(lightPos - hitInfo.hitPoint);
                        
                            // Shadow ray
                            Ray shadowRay;
                            shadowRay.origin = hitInfo.hitPoint + lightDir * 1e-4;  // Offset to prevent self-intersection
                            shadowRay.dir = lightDir;
                            ModelHitInfo shadowHit = CalculateLightCollision(shadowRay, modelIndex);
                        
                            if (!shadowHit.didHit || shadowHit.dst > lightDist) 
                            {
                                // Calculate light contribution
                                float3 lightEmission = ModelInfo[modelIndex].material.emissionColor * ModelInfo[modelIndex].material.emissionStrength;
                                float3 materialDiffuse = material.color;
                                float cosineTerm = max(dot(hitInfo.normal, lightDir), 0);  // Ensure non-negative
                                float attenuation = ModelInfo[modelIndex].material.emissionStrength / (lightDist * lightDist);

                                float p = max(rayColor.r, max(rayColor.g, rayColor.b));
                                // Add to incoming light
                                incomingLight += (lightEmission * attenuation * materialDiffuse * cosineTerm * rayColor) / lightPdf / p;

                                break;
                            }
                            
                        }
                        else
                        {
                            bool isSpecularBounce = material.specularProbability >= RandomValue(rngState);

                            rayOrigin = hitInfo.hitPoint;
                            float3 diffuseDir = normalize(hitInfo.normal + RandomDirection(rngState));
                            float3 specularDir = reflect(rayDir, hitInfo.normal);
                            float cosT = min(dot(-rayDir, hitInfo.normal), 1.0f); // Cosine of the angle of incidence
                            float sinT = sqrt(1.0f - (cosT * cosT)); // Sin(theta_t)
                            float3 refractDir;
                            float ri = 0 < dot(hitInfo.normal, rayDir) ? material.refractIndx : (1/material.refractIndx);
                            // Handle total internal reflection
                            if (sinT * ri > 1.0f || reflectance(cosT, ri) > RandomValue(rngState))
                            {
                                refractDir = reflect(rayDir, hitInfo.normal);  // Total internal reflection
                            }
                            else
                            {
                                // Refraction into a denser medium, apply Snell's law
                                refractDir = normalize(refract(rayDir, hitInfo.normal, ri));  // Compute refraction
                            }

                            if(material.transparency < RandomValue(rngState))
                            {
                                // Handle diffuse or specular reflection when no refraction occurs
                                rayDir = normalize(lerp(diffuseDir, specularDir, material.smoothness * isSpecularBounce));
                            }
                            else
                            {
                                rayDir = refractDir;
                                rayOrigin += refractDir * 1E-5;
                            }

                            float3 emittedLight = material.emissionColor * material.emissionStrength;
                            incomingLight += emittedLight * rayColor;
                            rayColor *= lerp(material.color, material.specularColor, isSpecularBounce);

                            float p = max(rayColor.r, max(rayColor.g, rayColor.b));
                            if(RandomValue(rngState) >= p)
                            {
                                // Sample light source
                                int modelIndex;
                                float lightPdf;
                                float3 lightPos = SampleLightSource(rngState, lightPdf, modelIndex);
                                float3 lightDir = normalize(lightPos - hitInfo.hitPoint);
                                float lightDist = length(lightPos - hitInfo.hitPoint);
                            
                                // Shadow ray
                                Ray shadowRay;
                                shadowRay.origin = hitInfo.hitPoint + lightDir * 1e-4;  // Offset to prevent self-intersection
                                shadowRay.dir = lightDir;
                                ModelHitInfo shadowHit = CalculateLightCollision(shadowRay, modelIndex);
                            
                                if (!shadowHit.didHit || shadowHit.dst > lightDist) 
                                {
                                    // Calculate light contribution
                                    float3 lightEmission = ModelInfo[modelIndex].material.emissionColor * ModelInfo[modelIndex].material.emissionStrength;
                                    float3 materialDiffuse = material.color;
                                    float cosineTerm = max(dot(hitInfo.normal, lightDir), 0);  // Ensure non-negative
                                    float attenuation = ModelInfo[modelIndex].material.emissionStrength / (lightDist * lightDist);
                            
                                    // Add to incoming light
                                    incomingLight += (lightEmission * attenuation * materialDiffuse * cosineTerm * rayColor) / lightPdf * p;

                                }
                                break;
                            }
                            rayColor *= 1.0 / p;
                            }
                    }
                    else
                    {
                        incomingLight += GetEnvironmentLight(rayDir) * rayColor;
                        break;
                    }
                    
                }

                return incomingLight;
            }

            float3 TraceDebugMode(float3 rayOrigin, float3 rayDir)
			{
				int2 stats; // num triangle tests, num bounding box tests
				Ray ray;
				ray.origin = rayOrigin;
				ray.dir = rayDir;
                uint rngState = 0;
				ModelHitInfo hitInfo = CalculateRayCollision(ray, stats);

				// Triangle test count vis
				if (visMode == 1)
				{
					float triVis = stats[0] / debugVisScale;
					return triVis < 1 ? triVis : float3(1, 0, 0);
				}
				// Box test count vis
				else if (visMode == 2)//
				{
					float boxVis = stats[1] / debugVisScale;
					return boxVis < 1 ? boxVis : float3(1, 0, 0);
				}
				// Distance
				else if (visMode == 3)
				{
					return length(rayOrigin - hitInfo.hitPoint) / debugVisScale;
				}
				// Normal
				else if (visMode == 4)
				{
					if (!hitInfo.didHit) return 0;
					return hitInfo.normal * 0.5 + 0.5;
				}

				return float3(1, 0, 1); // Invalid test mode
			}

            float3 CombineTracers(float3 directLight, float pdfLight, float3 indirectLight, float pdfIndirect)
            {
                float wLight = pdfLight / (pdfLight + pdfIndirect);
                float wIndirect = pdfIndirect / (pdfLight + pdfIndirect);
                return wLight * directLight + wIndirect * indirectLight;
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
                float3 focusPoint = mul(CamLocalToWorldMatrix, float4(viewPointLocal, 1));
                float3 camRight = CamLocalToWorldMatrix._m00_m10_m20;
                float3 camUp = CamLocalToWorldMatrix._m01_m11_m21;

                // Debug Mode
				#if DEBUG_VIS
                    return float4(TraceDebugMode(_WorldSpaceCameraPos, normalize(focusPoint - _WorldSpaceCameraPos)), 1);
                #endif
                
                // Calculate pixel color
                float3 totalIncomingLight = 0;

                for (int rayIndex = 0; rayIndex < NumRaysPerPixel; rayIndex++)
                {
                    float2 defocusJitter = RandomPointInCircle(rngState) * DefocusStrength / numPixels.x;
                    float3 rayOrigin = _WorldSpaceCameraPos + camRight * defocusJitter.x + camUp * defocusJitter.y;

                    float2 jitter = RandomPointInCircle(rngState) * DivergeStrength / numPixels.x;
                    float3 jitteredViewPoint = focusPoint + camRight * jitter.x + camUp * jitter.y;
                    float3 rayDir = normalize(jitteredViewPoint - _WorldSpaceCameraPos);

                    totalIncomingLight += Trace(rayOrigin, rayDir, rngState);
                }

                // Store view parameters as a color output (adjust based on needs)
                float3 pixelCol = totalIncomingLight / NumRaysPerPixel;
                return float4(pixelCol, 1);
            }
            
            ENDCG
        }
    }
}
