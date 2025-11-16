# WinUI3_MediaPlayer_Composition

 Test MediaPlayer with a [Microsoft.UI.Composition.CompositionDrawingSurface](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.composition.compositiondrawingsurface?view=windows-app-sdk-1.8) 
 
 The main code for rendering is simple (outside of "if (m_bEffect)").
 
 Tested on Windows 10 22H2, Windows App SDK 1.7.250401001
 
 To be fixed : there are some "D3D11 WARNING" on closing, apparently from [CreateDirect3D11SurfaceFromDXGISurface](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.directx.direct3d11.interop/nf-windows-graphics-directx-direct3d11-interop-createdirect3d11surfacefromdxgisurface)

 <img width="1140" height="668" alt="image" src="https://github.com/user-attachments/assets/38c60135-37d5-4eff-82f3-78d1aeb5f738" />
