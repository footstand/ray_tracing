using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Linq;

namespace RT {
    public class RayTracer : MonoBehaviour {
        // Raytracer Settings
        public bool RealTime;
        public bool AutoGenerateColliders = true;
        public bool SmoothEdges = true;
        public bool SingleMaterialOnly;
        public bool UseLighting = true;
        public float resolution = 1;
        public int MaxStack = 2;

        // The render texture
        [System.NonSerialized]
        Texture2D screen;

        // The shader used for reflections
        Shader reflectiveShader;

        Material tmpMat;

        // List variables for optimisation
        List<Light> lights;

        // Collision Mask
        readonly LayerMask collisionMask = 1 << 31;

        void Start() {
            // If the render texture already exists, destroy it!
            if (screen != null) {
                Destroy(screen);
            }

            // Create a new texture to render to
            screen = new Texture2D((int)(Screen.width * resolution), (int)(Screen.height * resolution));

            // Find the reflective shader to use (Specular)
            reflectiveShader = Shader.Find("Specular");

            if (AutoGenerateColliders) {
                // Generate Raytrace Colliders (mesh) for all renderers
                foreach (var mf in Object.FindObjectsOfType<MeshFilter>()) {
                    GenerateColliders(mf);
                }
            }

            if (!RealTime) {
                // Start Single Ray Trace
                RayTrace();
                SaveTextureToFile(screen, "output.png");
            }
        }

        void Update() {
            if (RealTime) {
                // Try real time ray tracing
                RayTrace();
            }
        }

        void OnGUI() {
            //Draw the rendered image along with an FPS count
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), screen);
            GUILayout.Label("fps: " + Mathf.Round(1 / Time.smoothDeltaTime));
        }

        void RayTrace() {
            // Find all lights and remember them (optimisation)
            lights = FindObjectsOfType<Light>().ToList();

            // Iterate through each pixel
            for (int x = 0; x < screen.width; x++) {
                for (int y = 0; y < screen.height; y++) {
                    // Trace each pixel and set the return value as the colour
                    screen.SetPixel(x, y, TracePixel(new Vector2(x, y)));
                }
            }
            // Apply changes to the render texture
            screen.Apply();
        }

        Color TracePixel(Vector2 pos) {
            //Calculate world position of the pixel and start a single Trace
            var ray = GetComponent<Camera>().ScreenPointToRay(new Vector3(pos.x / resolution, pos.y / resolution, 0));
            return TraceRay(ray.origin, ray.direction, 0);
        }

        Color TraceRay(Vector3 origin, Vector3 direction, int stack) {
            // Set nessesary temporary local variables
            Color tmpColor;
            RaycastHit hit;


            // Check Stack Flow and perform Raycast
            if (stack < MaxStack && Physics.Raycast(origin, direction, out hit, GetComponent<Camera>().farClipPlane, collisionMask)) {

                // Perform calculations only if we hit a collider with a parent (error handling)
                if (hit.collider && hit.collider.transform.parent) {
                    //if we have multiple materials and we are checking for multiple materials
                    if (hit.collider.transform.parent.GetComponent<MeshFilter>().mesh.subMeshCount > 1 && !SingleMaterialOnly) {
                        //find material from triangle index
                        tmpMat = hit.collider.transform.parent.GetComponent<Renderer>().materials[GetMatFromTrisInMesh(hit.collider.transform.parent.GetComponent<MeshFilter>().mesh, hit.triangleIndex)];
                    } else {
                        //set material to primary material
                        tmpMat = hit.collider.transform.parent.GetComponent<Renderer>().material;
                    }

                    //if the material has a texture
                    if (tmpMat.mainTexture) {
                        //set the colour to that of the texture coord of the raycast hit
                        tmpColor = (tmpMat.mainTexture as Texture2D).GetPixelBilinear(hit.textureCoord.x, hit.textureCoord.y);
                    } else {
                        //set the colour to the colour of the material
                        tmpColor = tmpMat.color;
                    }

                    //Transparent pixel, trace again and add on to colour
                    if (tmpColor.a < 1) {
                        tmpColor *= tmpColor.a;
                        tmpColor += (1 - tmpColor.a) * TraceRay(hit.point - hit.normal * 0.01f, direction, stack + 1);
                    }

                    //Surface is reflective, trace reflection and add on to colour
                    if (tmpMat.shader == reflectiveShader) {
                        var tmpFloat = tmpColor.a * tmpMat.GetFloat("_Shininess");
                        tmpColor += tmpFloat * TraceRay(hit.point + hit.normal * 0.0001f, Vector3.Reflect(direction, hit.normal), stack + 1);
                    }

                    //Calculate lighting
                    if (UseLighting) {
                        //With smooth edges
                        if (SmoothEdges) {
                            tmpColor *= TraceLight(hit.point + hit.normal * 0.0001f, InterpolateNormal(hit.point, hit.normal, hit.collider.transform.parent.GetComponent<MeshFilter>().mesh, hit.triangleIndex, hit.transform));
                        }
                        //Without smooth edges
                        else {
                            tmpColor *= TraceLight(hit.point + hit.normal * 0.0001f, hit.normal);
                        }
                    }

                    tmpColor.a = 1;
                    return tmpColor;
                } else {
                    //Return Error colour on wierd error
                    return Color.red;
                }
            } else {
                //Render Skybox if present, else just blue
                if (RenderSettings.skybox) {
                    //Perform A Skybox Trace
                    tmpColor = SkyboxTrace(direction, RenderSettings.skybox);

                    //Replace alpha with White colour
                    //for some reason nessesary
                    tmpColor += Color.white * (1 - tmpColor.a) / 10;
                    tmpColor.a = 1;

                    return tmpColor;
                } else {
                    return Color.blue;
                }
            }

        }

        //Convert a direction to a pixel of a cubemap (used only for skyboxes)
        Color SkyboxTrace(Vector3 direction, Material skybox) {
            //Funky stuff I still don't quite get
            //If you can explain this, please add comments

            return Color.blue;

            // TODO: Unity5 is not supported skybox.GetTexture("_LeftTex")

            /*if (Mathf.Abs(direction.x) > Mathf.Abs(direction.y)) {
                if (Mathf.Abs(direction.x) > Mathf.Abs(direction.z)) {
                    if (direction.x < 0) {
                        return (skybox.GetTexture("_LeftTex") as Texture2D).GetPixelBilinear((-direction.z/-direction.x+1)/2, (direction.y/-direction.x+1)/2);
                    }
                    else{
                        return (skybox.GetTexture("_RightTex") as Texture2D).GetPixelBilinear((direction.z/direction.x+1)/2, (direction.y/direction.x+1)/2);
                    }
                }
                else{
                    if (direction.z < 0) {
                        return (skybox.GetTexture("_BackTex") as Texture2D).GetPixelBilinear((direction.x/-direction.z+1)/2, (direction.y/-direction.z+1)/2);
                    }
                    else{
                        return (skybox.GetTexture("_FrontTex") as Texture2D).GetPixelBilinear((-direction.x/direction.z+1)/2, (direction.y/direction.z+1)/2);
                    }
                }
            }
            else if (Mathf.Abs(direction.y) > Mathf.Abs(direction.z)){
                if (direction.y < 0) {
                    return (skybox.GetTexture("_DownTex") as Texture2D).GetPixelBilinear((-direction.x/-direction.y+1)/2, (direction.z/-direction.y+1)/2);
                }
                else{
                    return (skybox.GetTexture("_UpTex") as Texture2D).GetPixelBilinear((-direction.x/direction.y+1)/2, (-direction.z/direction.y+1)/2);
                }
            }
            else{
                if (direction.z < 0) {
                    return (skybox.GetTexture("_BackTex") as Texture2D).GetPixelBilinear((direction.x/-direction.z+1)/2, (direction.y/-direction.z+1)/2);
                }
                else{
                    return (skybox.GetTexture("_FrontTex") as Texture2D).GetPixelBilinear((-direction.x/direction.z+1)/2, (direction.y/direction.z+1)/2);
                }
            }*/
        }

        // Returns the material index of a mesh a triangle is using
        // Acctually it returns the index of the submesh the triangle is in
        int GetMatFromTrisInMesh(Mesh mesh, int trisIndex) {
            // get the triangel from the triangle index
            var tri = new [] {
                mesh.triangles[trisIndex * 3],
                mesh.triangles[trisIndex * 3 + 1],
                mesh.triangles[trisIndex * 3 + 2]
            };

            // Iterate through all submeshes, each submesh has a different material of the same index as the submesh
            for (int index = 0; index < mesh.subMeshCount; index++) {
                // Get submesh trianges
                var tris = mesh.GetTriangles(index);
                // Iterate through all triangles
                for (int index2 = 0; index2 < tris.Length; index2 += 3) {
                    //Find the same triangle and return the index of the submesh it is in
                    if (tris[index2] == tri[0] && tris[index2 + 1] == tri[1] && tris[index2 + 2] == tri[2]) {
                        return index;
                    }
                }
            }

            return 0;
        }

        //Interpolates between the 3 normals of a triangle given the point
        Vector3 InterpolateNormal(Vector3 point, Vector3 normal, Mesh mesh, int trisIndex, Transform trans) {
            //find the indexes of each verticie of the triange
            var index = mesh.triangles[trisIndex * 3];
            var index2 = mesh.triangles[trisIndex * 3 + 1];
            var index3 = mesh.triangles[trisIndex * 3 + 2];

            //temporary variable used for re-arrenement
            int tmpIndex;

            //Find the distance between each verticie and the point
            var d1 = Vector3.Distance(mesh.vertices[index], point);
            var d2 = Vector3.Distance(mesh.vertices[index2], point);
            var d3 = Vector3.Distance(mesh.vertices[index3], point);

            //compare and rearrange the verticie index so that index is the one furthest away from the point
            if (d2 > d1 && d2 > d3) {
                tmpIndex = index;
                index = index2;
                index2 = tmpIndex;
            } else if (d3 > d1 && d3 > d2) {
                tmpIndex = index;
                index = index3;
                index3 = tmpIndex;
                tmpIndex = index2;
                index2 = index3;
                index3 = tmpIndex;
            }

            //Find the point along the line between the 2 other verticies that the ray from the furthest verticies through the point intersects
            //Using Plane raycasting
            //Generate Plane
            var plane = new Plane(trans.TransformPoint(mesh.vertices[index2]), trans.TransformPoint(mesh.vertices[index3]) + normal, trans.TransformPoint(mesh.vertices[index3]) - normal);
            //Renerate Ray
            var ray = new Ray(trans.TransformPoint(mesh.vertices[index]), (point - trans.TransformPoint(mesh.vertices[index])).normalized);

            float tmpFloat;
            //Intersect Ray and Plane
            if (!plane.Raycast(ray, out tmpFloat)) {
                //Something went terribly wrong... damn it
                Debug.Log("This Shouldn't EVER happen");
                return normal;
            }

            //Do the interpolation :D
            //If you really wanna see how this works, just google it
            //It's too complicated to explain here
            var point2 = ray.origin + ray.direction * tmpFloat;
            var normal2 = Vector3.Lerp(trans.TransformDirection(mesh.normals[index2]), trans.TransformDirection(mesh.normals[index3]), Vector3.Distance(trans.TransformPoint(mesh.vertices[index2]), point2) / Vector3.Distance(trans.TransformPoint(mesh.vertices[index2]), trans.TransformPoint(mesh.vertices[index3])));
            var normal3 = Vector3.Lerp(normal2, trans.TransformDirection(mesh.normals[index]), Vector3.Distance(point2, point) / Vector3.Distance(point2, trans.TransformPoint(mesh.vertices[index])));
            //return interpolated normal
            return normal3;
        }

        //Calculate the lighting of a point
        Color TraceLight(Vector3 pos, Vector3 normal) {
            //set nessesary temporary provate variables
            //set default light to ambient lighting
            var tmpColor = RenderSettings.ambientLight;

            //Iterate through all lights in the scene
            //lights is computer once per render (optimisation)
            foreach (var l in lights) {
                //Only calculate lighting if the light is on
                if (l.enabled) {
                    //trace the light and add it to the light colour
                    tmpColor += TraceLightInternal(l, pos, normal);
                }
            }

            //return light colour at that point
            return tmpColor;
        }

        //Trace lighting for one light at one spot
        Color TraceLightInternal(Light l, Vector3 pos, Vector3 normal) {
            //If light is directional (easy)
            //Just trace in the opposite direction
            if (l.type == LightType.Directional) {
                var direction = l.transform.TransformDirection(Vector3.back);
                var col = new Color(l.intensity, l.intensity, l.intensity);
                col *= 1f - Quaternion.Angle(Quaternion.identity, Quaternion.FromToRotation(normal, direction)) / 90f;
                return TransparancyTrace(col, pos, direction, Mathf.Infinity);
            }

            //If light is point light (medium)
            //Just trace towards it, if within range
            //also apply linear falloff according to distance and range
            if (l.type == LightType.Point) {
                if (Vector3.Distance(pos, l.transform.position) <= l.range) {
                    var direction = (l.transform.position - pos).normalized;
                    var intensity = (l.range - Vector3.Distance(pos, l.transform.position)) / l.range * l.intensity;
                    var col = new Color(intensity, intensity, intensity);
                    col *= 1 - Quaternion.Angle(Quaternion.identity, Quaternion.FromToRotation(normal, direction)) / 90f;
                    return TransparancyTrace(col, pos, direction, Vector3.Distance(l.transform.position, pos));
                }
            }

            //If light is spot light (Hard)
            //Do the same as a point light, but also get the angle from  direction towards the light to the opposite direction of the light
            //If this angle is more than the spot angle, no light
            //else apply linear fall off according to this angle and spot angle
            if (l.type == LightType.Spot) {
                if (Vector3.Distance(pos, l.transform.position) <= l.range) {
                    var direction = (l.transform.position - pos).normalized;
                    if (Vector3.Angle(direction, -l.transform.forward) < l.spotAngle) {
                        var intensity = (l.range - Vector3.Distance(pos, l.transform.position)) / l.range * l.intensity;
                        intensity *= 1f - Vector3.Angle(direction, -l.transform.forward) / l.spotAngle;
                        var col = new Color(intensity, intensity, intensity);
                        col *= 1 - Quaternion.Angle(Quaternion.identity, Quaternion.FromToRotation(normal, direction)) / 90f;
                        return TransparancyTrace(col, pos, direction, Vector3.Distance(l.transform.position, pos));
                    }
                }
            }

            //If the light is of any other type, do not calculate any lighting
            return Color.black;
        }

        //This traces for transparent shadows
        //Instead of tracing once to see if an object was hit, it does a RaycastAll
        //And Iterates through all objects, gets the pixel colour of that raycast hit
        //Then multiples it by the inverse of the alpha of that pixel
        Color TransparancyTrace(Color col, Vector3 pos, Vector3 dir, float dist) {
            var tmpColor = col;

            // Raycast throug everything, returning a list of hits, instead of just the closest
            var hits = Physics.RaycastAll(pos, dir, dist, collisionMask);

            // Iterate through each hit
            foreach (var hit in hits) {
                // Same as in TraceRay, it gets the pixel colour of that hit point
                // So no point in commenting on this
                if (hit.collider.transform.parent.GetComponent<MeshFilter>().mesh.subMeshCount > 1 && !SingleMaterialOnly) {
                    tmpMat = hit.collider.transform.parent.GetComponent<Renderer>().materials[GetMatFromTrisInMesh(hit.collider.transform.parent.GetComponent<MeshFilter>().mesh, hit.triangleIndex)];
                } else {
                    tmpMat = hit.collider.transform.parent.GetComponent<Renderer>().material;
                }

                //Apply colour transformation according to pixels alpha value
                if (tmpMat.mainTexture != null) {
                    var tmpTex = (tmpMat.mainTexture as Texture2D);
                    tmpColor *= 1 - tmpTex.GetPixelBilinear(hit.textureCoord.x, hit.textureCoord.y).a;
                } else {
                    tmpColor *= 1 - tmpMat.color.a;
                }
            }

            // return resulting colour
            return tmpColor;
        }

        // Some stupid unnessesary and unused stuff to save the rendered texture to file
        // really not worth explaining
        void SaveTextureToFile(Texture2D texture, string fileName) {
            var bytes = texture.EncodeToPNG();
            Debug.Log(Application.dataPath + "/" + fileName);
            var file = File.Open(Application.dataPath + "/" + fileName, FileMode.Create);
            new BinaryWriter(file).Write(bytes);
            file.Close();
        }

        // Generates colliders used for raytracing for one GamObject
        // I wish I could add this to Instantiate!!
        GameObject GenerateColliders(GameObject go) {
            // Generate colliders only if there is a mesh filter
            var mf = go.GetComponent<MeshFilter>();
            if (mf != null) {
                GenerateColliders(mf);
            }
            // return same gameObject
            return go;
        }

        GameObject GenerateColliders(MeshFilter mf) {
            // Create Object
            var go = new GameObject("MeshRender");

            // Set Defaults and copy settings
            go.transform.parent = mf.transform;
            go.AddComponent<MeshFilter>().mesh = mf.mesh;
            go.AddComponent<MeshCollider>().sharedMesh = mf.mesh;

            // Make collider a trigger
            //go.GetComponent<Collider>().isTrigger = true;

            // reset positioning
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one;
            go.transform.rotation = mf.transform.rotation;
            // set layer
            go.layer = 31;

            return go;
        }
    }
}
