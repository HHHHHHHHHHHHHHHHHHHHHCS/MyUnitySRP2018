using System.Collections;
using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;

public class MyPipelineShaderPreprocessor : IPreprocessShaders
{
    public int callbackOrder { get; } = 0;

    public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
    {
        Debug.Log(shader.name);
    }
}