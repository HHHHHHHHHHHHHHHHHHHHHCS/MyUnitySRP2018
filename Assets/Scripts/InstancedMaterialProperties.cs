using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InstancedMaterialProperties : MonoBehaviour
{
    private static MaterialPropertyBlock propertyBlock;
    private static readonly int colorID = Shader.PropertyToID("_Color");
    private static readonly int metallicID = Shader.PropertyToID("_Metallic");
    private static readonly int smoothnessID = Shader.PropertyToID("_Smoothness");

    [SerializeField] private Color color = Color.white;

    [SerializeField, Range(0f, 1f)] private float metallic = 0f;

    [SerializeField, Range(0f, 1f)] private float smoothness = 0.5f;


    private void Awake()
    {
        OnValidate();
    }

    private void OnValidate()
    {
        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        propertyBlock.SetColor(colorID, color);
        propertyBlock.SetFloat(metallicID, metallic);
        propertyBlock.SetFloat(smoothnessID, smoothness);
        GetComponent<MeshRenderer>().SetPropertyBlock(propertyBlock);
    }
}