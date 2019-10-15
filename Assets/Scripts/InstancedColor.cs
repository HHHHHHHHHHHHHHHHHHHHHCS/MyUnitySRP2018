using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InstancedColor : MonoBehaviour
{
    private static MaterialPropertyBlock propertyBlock;
    private static readonly int colorID = Shader.PropertyToID("_Color");

    [SerializeField] private Color color = Color.white;

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
        GetComponent<MeshRenderer>().SetPropertyBlock(propertyBlock);
    }
}