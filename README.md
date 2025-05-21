# WpfD3d12

Sample project showing how to use D3d12 in wpf

### How
- Init D3d12
- Init D3d11On12
- Init Window
- Init D3d9
- D3d11On12 Create Texture
- Share to D3d9
- Share to D3d12
- D3d12 Draw
- D3d9 Present by D3DImage

```csharp
var color = new float4(0.83f, 0.8f, 0.97f, 1f);
    Graphics.CommandList.Handle->ClearRenderTargetView(
        SwapChain.CurrentRtv,
        ref Unsafe.As<float4, float>(ref color),
        0, null
);
```

![](./1.png)
