using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Rendering/My Post-Processing Stack")]
public class MyPostProcessingStack : ScriptableObject
{
    private enum Pass
    {
        Copy = 0,
        Blur,
    }

    private static Mesh fullScreenTriangle;

    private static Material material;

    private static int mainTexID = Shader.PropertyToID("_MainTex");
    private static int tempTexID = Shader.PropertyToID("_MyPostProcessingStackTempTex");

    [SerializeField, Range(0, 10)] private int blurStrength;

    private static void InitializeStatic()
    {
        if (fullScreenTriangle)
        {
            return;
        }

        fullScreenTriangle = new Mesh()
        {
            name = "My Post-Processing Stack Full-Screen Triangle",
            vertices = new Vector3[]
            {
                new Vector3(-1f, -1f, 0f),
                new Vector3(-1f, 3f, 0f),
                new Vector3(3f, -1f, 0f),
            },
            triangles = new int[] {0, 1, 2},
        };
        fullScreenTriangle.UploadMeshData(true);

        material = new Material(Shader.Find("Hidden/My Pipeline/PostEffectStack"))
        {
            name = "My Post-Processing Stack Material",
            hideFlags = HideFlags.HideAndDontSave
        };
    }

    public void Render(CommandBuffer cb, int cameraColorID, int cameraDepthID, int width, int height)
    {
        InitializeStatic();

        if (blurStrength > 0)
        {
            Blur(cb, cameraColorID, width, height);
        }
        else
        {
            Blit(cb, cameraColorID, BuiltinRenderTextureType.CameraTarget);
        }
    }

    private void Blit(CommandBuffer cb, RenderTargetIdentifier srcID, RenderTargetIdentifier destID,
        Pass pass = Pass.Copy)
    {
        cb.SetGlobalTexture(mainTexID, srcID);

        cb.SetRenderTarget(destID, RenderBufferLoadAction.DontCare,
            RenderBufferStoreAction.Store);

        cb.DrawMesh(fullScreenTriangle, Matrix4x4.identity, material, 0, (int) pass);
    }

    private void Blur(CommandBuffer cb, int cameraColorID, int width, int height)
    {
        cb.BeginSample("Blur");

        if (blurStrength == 1)
        {
            Blit(cb, cameraColorID, BuiltinRenderTextureType.CameraTarget, Pass.Blur);
            cb.EndSample("Blur");
            return;
        }

        cb.GetTemporaryRT(tempTexID, width, height, 0, FilterMode.Bilinear);
        int passesLeft;

        for (passesLeft = blurStrength; passesLeft > 2; passesLeft -= 2)
        {
            Blit(cb, cameraColorID, tempTexID, Pass.Blur);
            Blit(cb, tempTexID, cameraColorID, Pass.Blur);
        }

        if (passesLeft > 1)
        {
            Blit(cb, cameraColorID, tempTexID, Pass.Blur);
            Blit(cb, tempTexID, BuiltinRenderTextureType.CameraTarget, Pass.Blur);
        }
        else
        {
            Blit(cb, cameraColorID, BuiltinRenderTextureType.CameraTarget, Pass.Blur);
        }

        cb.ReleaseTemporaryRT(tempTexID);

        cb.EndSample("Blur");
    }
}