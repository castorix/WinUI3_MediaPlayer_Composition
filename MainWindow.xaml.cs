using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Microsoft.UI.Xaml.Hosting;

using System.ComponentModel;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

using GlobalStructures;
using static GlobalStructures.GlobalTools;
using Direct2D;
using DXGI;
using static DXGI.DXGITools;
using D3D11;
//using Windows.Graphics.Imaging;
//using WIC;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinUI3_MediaPlayer_Composition
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        [ComImport, Guid("FAB19398-6D19-4D8A-B752-8F096C396069"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ICompositorInterop
        {  
            [PreserveSig]
            HRESULT CreateGraphicsDevice(IntPtr renderingDevice, out IntPtr result  /*_COM_Outptr_ ICompositionGraphicsDevice ** result*/);
        }

        [ComImport, Guid("2D6355C2-AD57-4EAE-92E4-4C3EFF65D578"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ICompositionDrawingSurfaceInterop
        {            
            [PreserveSig]
            HRESULT BeginDraw(IntPtr updateRect, [MarshalAs(UnmanagedType.LPStruct)] Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object updateObject, out POINT updateOffset);
            [PreserveSig]
            HRESULT EndDraw();
            [PreserveSig]
            HRESULT Resize(SIZE sizePixels);
            [PreserveSig]
            HRESULT Scroll(ref RECT scrollRect, ref RECT clipRect, int offsetX, int offsetY);
            [PreserveSig]
            HRESULT ResumeDraw();
            [PreserveSig]
            HRESULT SuspendDraw();
        }    

        private MediaPlayer m_mediaPlayer = new MediaPlayer();
        private MediaPlaybackList m_playbackList = new MediaPlaybackList();
        //private MediaSource? m_mediaSource = null;

        ID2D1Factory m_pD2DFactory = null;
        ID2D1Factory1 m_pD2DFactory1 = null;

        IntPtr m_pD3D11DevicePtr = IntPtr.Zero;
        Direct2D.ID3D11DeviceContext m_pD3D11DeviceContext = null; // Released in Clean : not used
        IDXGIDevice1 m_pDXGIDevice = null;
        ID2D1Device m_pD2DDevice = null;
        ID2D1DeviceContext m_pD2DDeviceContext = null;
 
        Microsoft.UI.Composition.SpriteVisual m_SpriteVisual = null;
        ICompositionDrawingSurfaceInterop m_pSurfaceInterop = null;

        IntPtr hWndMain;
        double m_nDisplayWidth = Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea.Width;
        double m_nDisplayHeight = Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea.Height;

        public MainWindow()
        {
            this.InitializeComponent();

            hWndMain = WinRT.Interop.WindowNative.GetWindowHandle(this);
            this.SizeChanged += MainWindow_SizeChanged;
            this.Closed += MainWindow_Closed;
            m_nDisplayWidth = Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea.Width;
            m_nDisplayHeight = Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea.Height;
            double nWidth = 1500;
            double nHeight = 750;
            double nXPos = (m_nDisplayWidth - nWidth) / 2;
            double nYPos = (m_nDisplayHeight - nHeight) / 2;
            this.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32((int)(nXPos), (int)(nYPos), (int)(nWidth), (int)(nHeight)));

            LoadVideos();

            HRESULT hr = CreateD2D1Factory();
            if (SUCCEEDED(hr))
            {
                hr = CreateDeviceContext();

                var compositor = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(this.Content).Compositor;
                var compositorInterop = compositor.As<ICompositorInterop>();

                int nClientWidth = this.AppWindow.ClientSize.Width;
                int nClientHeight = this.AppWindow.ClientSize.Height;

                IntPtr pD2DDevicePtr = Marshal.GetIUnknownForObject(m_pD2DDevice);
                IntPtr pCompositionGraphicsDevicePtr = IntPtr.Zero;
                hr = compositorInterop.CreateGraphicsDevice(pD2DDevicePtr, out pCompositionGraphicsDevicePtr);
                if (SUCCEEDED(hr))
                {
                    Microsoft.UI.Composition.CompositionGraphicsDevice cgd = MarshalInterface<Microsoft.UI.Composition.CompositionGraphicsDevice>.FromAbi(pCompositionGraphicsDevicePtr);
                    Size size = new Size(nClientWidth, nClientHeight);

                    //Windows.Graphics.SizeInt32 size1 = new  Windows.Graphics.SizeInt32(nWidth, nHeight);
                    //var _virtualSurface = cgd.CreateVirtualDrawingSurface(size1, Microsoft.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized, Microsoft.Graphics.DirectX.DirectXAlphaMode.Premultiplied);
                   
                    Size displaySize = new Size(m_nDisplayWidth, m_nDisplayHeight);

                    Microsoft.UI.Composition.CompositionDrawingSurface  drawingSurface = cgd.CreateDrawingSurface(displaySize, Microsoft.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized, Microsoft.Graphics.DirectX.DirectXAlphaMode.Premultiplied);
                    m_pSurfaceInterop = drawingSurface.As<ICompositionDrawingSurfaceInterop>();                   
    
                    Microsoft.UI.Composition.CompositionSurfaceBrush surfaceBrush = compositor.CreateSurfaceBrush(drawingSurface);
                    surfaceBrush.Stretch = Microsoft.UI.Composition.CompositionStretch.None;
                    surfaceBrush.HorizontalAlignmentRatio = 0;
                    surfaceBrush.VerticalAlignmentRatio = 0;                  

                    m_SpriteVisual = compositor.CreateSpriteVisual();
                    m_SpriteVisual.Brush = surfaceBrush;
                    m_SpriteVisual.Size = new System.Numerics.Vector2(nClientWidth, nClientHeight);
                    m_SpriteVisual.Offset = new System.Numerics.Vector3(0, 0, 0);
                    m_SpriteVisual.Opacity = SpriteOpacity;
                    m_SpriteVisual.CenterPoint = new System.Numerics.Vector3(nClientWidth / 2f, nClientHeight / 2f, 0f);
                    //m_SpriteVisual.RotationAngleInDegrees = 45; 

                    // Tests from ChatGPT...
                    //float ax = 30f * (float)Math.PI / 180f;
                    //float ay = 20f * (float)Math.PI / 180f;
                    //System.Numerics.Quaternion qx = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitX, ax);
                    //System.Numerics.Quaternion qy = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, ay);
                    // Combine the two rotations
                    //m_SpriteVisual.Orientation = System.Numerics.Quaternion.Normalize(System.Numerics.Quaternion.Concatenate(qx, qy));

                    //// Create a 45° rotation around Z-axis
                    //float angleRadians = 45f * (float)Math.PI / 180f;
                    //m_SpriteVisual.Orientation = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, angleRadians);

                    var root = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(this.Content) as Microsoft.UI.Composition.ContainerVisual;
                    Microsoft.UI.Composition.VisualCollection vsChildren = root.Children;                    
                    vsChildren.InsertAtTop(m_SpriteVisual);
                   
                    Marshal.Release(pCompositionGraphicsDevicePtr);
                }
                Marshal.Release(pD2DDevicePtr);

                CompositionTarget.Rendering += CompositionTarget_Rendering;
            }
        }

        private async void btn_LoadVideo_Click(object sender, RoutedEventArgs e)
        {      
            if (m_mediaPlayer.Source == null)
            {              
                m_mediaPlayer.IsVideoFrameServerEnabled = true;
                m_mediaPlayer.VideoFrameAvailable += OnVideoFrameAvailable;
                //m_mediaPlayer.Source = m_mediaSource;
                m_mediaPlayer.Source = m_playbackList;
                //m_mediaPlayer.IsLoopingEnabled = true;
                m_mediaPlayer.Play();               
            }
            else
            {
                Windows.UI.Popups.MessageDialog md = new Windows.UI.Popups.MessageDialog("Video already loaded", "Information");
                WinRT.Interop.InitializeWithWindow.Initialize(md, hWndMain);
                _ = await md.ShowAsync();
            }
        }

        private readonly object _surfaceLock = new();
        bool m_bFrame = false;
        MediaPlayer? m_currentMediaPlayer = null;

        private async Task LoadVideosAsync()
        {
            m_playbackList.AutoRepeatEnabled = true;

            string sExeFolder = AppContext.BaseDirectory;
            string[] sVideoFiles = new[]
            {
                Path.Combine(sExeFolder, "Assets", "Alita Battle Angel Teaser Trailer HD 20th Century FOX.mp4"),
                Path.Combine(sExeFolder, "Assets", "Black Panther Teaser Trailer HD.mp4"),
                Path.Combine(sExeFolder, "Assets", "MORBIUS - Official Trailer HD.mp4"),
                Path.Combine(sExeFolder, "Assets", "Wonder Woman 1984 Official Trailer.mp4"),
                Path.Combine(sExeFolder, "Assets", "Marvel Studios Avengers Infinity War Official Trailer.mp4"),
                Path.Combine(sExeFolder, "Assets", "Kingdom of the Planet of the Apes Teaser Trailer.mp4")
                //Path.Combine(sExeFolder, "Assets",  "sample_960x400_ocean_with_audio.mp4"),               
            };
            foreach (var sPath in sVideoFiles)
            {
                if (File.Exists(sPath))
                {
                    var storageFile = await StorageFile.GetFileFromPathAsync(sPath);
                    var source = MediaSource.CreateFromStorageFile(storageFile);
                    var item = new MediaPlaybackItem(source);
                    m_playbackList.Items.Add(item);
                }
            }
        }

        private async void LoadVideos()
        {
            await LoadVideosAsync();
        }

        private void CompositionTarget_Rendering(object sender, object e)
        {  
            HRESULT hr = Render();
        }

        bool m_bEffect = true;
        D3D11.ID3D11Texture2D m_pSharedTexture = null;
        bool m_bInitTexture = false;

        HRESULT Render()
        {
            HRESULT hr = HRESULT.S_OK;
            if (m_pSurfaceInterop != null && m_currentMediaPlayer != null)
            {
                if (m_bFrame)
                {
                    object pUpdateObject = null;
                    hr = m_pSurfaceInterop.BeginDraw(IntPtr.Zero, typeof(IDXGISurface).GUID, out pUpdateObject, out POINT pUpdateOffset);
                    if (SUCCEEDED(hr))
                    {
                        IDXGISurface pDXGISurface = pUpdateObject as IDXGISurface;

                        IntPtr pDXGISurfacePtr = Marshal.GetIUnknownForObject(pUpdateObject);

                        IntPtr pGraphicsSurfacePtr = IntPtr.Zero;
                        hr = CreateDirect3D11SurfaceFromDXGISurface(pDXGISurface, out pGraphicsSurfacePtr);                       
                        if (SUCCEEDED(hr))
                        {
                            uint nDPI = GetDpiForWindow(hWndMain);
                            float nScale = nDPI / 96.0f;
                            float nWidth = (float)this.AppWindow.ClientSize.Width / nScale;
                            float nHeight = (float)this.AppWindow.ClientSize.Height / nScale;

                            //IDirect3DSurface pDirect3DSurface = (IDirect3DSurface)Marshal.GetObjectForIUnknown(pGraphicsSurfacePtr);
                            IDirect3DSurface pDirect3DSurface = MarshalInspectable<IDirect3DSurface>.FromAbi(pGraphicsSurfacePtr);                          
                            var destRect = new Windows.Foundation.Rect(pUpdateOffset.x, pUpdateOffset.y, Math.Max(1, (int)nWidth + 1), Math.Max(1, (int)nHeight + 1));
                            //if (m_bEffect)
                            //    m_currentMediaPlayer.CopyFrameToVideoSurface(pDirect3DSurface);
                            //else
                            //    m_currentMediaPlayer.CopyFrameToVideoSurface(pDirect3DSurface, destRect);                           

                            m_bEffect = (cbEmboss.IsChecked == true || cbGaussianBlur.IsChecked == true
                                || cbGrayscale.IsChecked == true || cbInvert.IsChecked == true);
                            if (m_bEffect)
                            {
                                IntPtr pDevicePtr = IntPtr.Zero;
                                Guid IID_ID3D11Device = typeof(ID3D11Device).GUID;
                                hr = pDXGISurface.GetDevice(IID_ID3D11Device, out pDevicePtr);
                                if (SUCCEEDED(hr))
                                {
                                    ID3D11Device pD3D11Device = Marshal.GetObjectForIUnknown(pDevicePtr) as ID3D11Device;
                                    if (pD3D11Device != null)
                                    {
                                        D3D11.ID3D11Texture2D pOriginalTexture = (D3D11.ID3D11Texture2D)Marshal.GetTypedObjectForIUnknown(pDXGISurfacePtr, typeof(D3D11.ID3D11Texture2D));

                                        var textureDesc = new D3D11.D3D11_TEXTURE2D_DESC();
                                        pOriginalTexture.GetDesc(out textureDesc);
                                        textureDesc.Usage = /*D3D11.D3D11_USAGE.D3D11_USAGE_STAGING;*/ D3D11.D3D11_USAGE.D3D11_USAGE_DEFAULT;
                                        textureDesc.BindFlags = D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET;
                                        textureDesc.CPUAccessFlags = D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ;
                                        textureDesc.MiscFlags = D3D11_RESOURCE_MISC_FLAG.D3D11_RESOURCE_MISC_SHARED;
                                        textureDesc.Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM;

                                        if (!m_bInitTexture)
                                            hr = pD3D11Device.CreateTexture2D(ref textureDesc, IntPtr.Zero, out m_pSharedTexture);
                                        else
                                            m_bInitTexture = true;

                                        D3D11.ID3D11DeviceContext pD3D11DeviceContext = null;
                                        pD3D11Device.GetImmediateContext(out pD3D11DeviceContext);
                                        if (pD3D11DeviceContext != null)
                                        {                                            
                                            D3D11.ID3D11RenderTargetView pRTV = null;
                                            hr = pD3D11Device.CreateRenderTargetView(pOriginalTexture, IntPtr.Zero, out pRTV);
                                            if (SUCCEEDED(hr) && pRTV != null)
                                            {
                                                // Clear to black (RGBA)
                                                float[] clearColor = new float[] { 0f, 0f, 0f, 1f };
                                                pD3D11DeviceContext.ClearRenderTargetView(pRTV, clearColor);                                               
                                                SafeRelease(ref pRTV);
                                            }

                                            m_currentMediaPlayer.CopyFrameToVideoSurface(pDirect3DSurface, destRect);

                                            pD3D11DeviceContext.CopyResource(m_pSharedTexture, pOriginalTexture);
                                            //pD3D11DeviceContext.Flush();
                                            SafeRelease(ref pD3D11DeviceContext);
                                        }

                                        IDXGISurface pDXGISurfaceTexture = m_pSharedTexture as IDXGISurface;

                                        D2D1_BITMAP_PROPERTIES1 bitmapProperties = new D2D1_BITMAP_PROPERTIES1();
                                        bitmapProperties.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;                                       
                                        bitmapProperties.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
                                        //uint nDPI = GetDpiForWindow(hWndMain);
                                        //bitmapProperties.dpiX = nDPI;
                                        //bitmapProperties.dpiY = nDPI;
                                        bitmapProperties.dpiX = 96.0f;
                                        bitmapProperties.dpiY = 96.0f;

                                        hr = m_pSurfaceInterop.EndDraw();

                                        object pUpdateObject2 = null;
                                        hr = m_pSurfaceInterop.BeginDraw(IntPtr.Zero, typeof(ID2D1DeviceContext).GUID, out pUpdateObject2, out POINT pUpdateOffset2);
                                        if (SUCCEEDED(hr))
                                        {
                                            ID2D1DeviceContext pD2DDeviceContext = pUpdateObject2 as ID2D1DeviceContext;

                                            ID2D1Bitmap1 pD2DBitmap = null;
                                            hr = pD2DDeviceContext.CreateBitmapFromDxgiSurface(pDXGISurfaceTexture, bitmapProperties, out pD2DBitmap);
                                            if (SUCCEEDED(hr))
                                            {
                                                pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black, 1.0f));

                                                //var nVideoWidth =  m_mediaPlayer.PlaybackSession.NaturalVideoWidth;
                                                //var nVideoHeight = m_mediaPlayer.PlaybackSession.NaturalVideoHeight;
                                                //var nVideoRect = m_mediaPlayer.PlaybackSession.NormalizedSourceRect;

                                                //var nVideoWidth = textureDesc.Width;
                                                //var nVideoHeight = textureDesc.Height;

                                                // Test a few Direct2D effects
                                                // More effects in sample : https://github.com/castorix/WinUI3_Direct2D_Effects

                                                ID2D1Bitmap1 pD2DBitmapNew = null;
                                                ID2D1Image pOutputImage = null;
                                                ID2D1Effect pEffect = null;

                                                if (cbEmboss.IsChecked == true)
                                                {  
                                                    hr = pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Emboss, out pEffect);
                                                    if (SUCCEEDED(hr))
                                                    {
                                                        pEffect.SetInput(0, pD2DBitmap);
                                                        SetEffectFloat(pEffect, (uint)D2D1_EMBOSS_PROP.D2D1_EMBOSS_PROP_HEIGHT, StrengthEmboss);
                                                        SetEffectFloat(pEffect, (uint)D2D1_EMBOSS_PROP.D2D1_EMBOSS_PROP_DIRECTION, 45.0f);                                                        
                                                        pEffect.GetOutput(out pOutputImage);
                                                    }
                                                }
                                                if (cbGaussianBlur.IsChecked == true)
                                                {  
                                                    hr = pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1GaussianBlur, out pEffect);
                                                    if (SUCCEEDED(hr))
                                                    {
                                                        pEffect.SetInput(0, pD2DBitmap);
                                                        SetEffectFloat(pEffect, (uint)D2D1_GAUSSIANBLUR_PROP.D2D1_GAUSSIANBLUR_PROP_STANDARD_DEVIATION, GaussianBlur);
                                                        SetEffectInt(pEffect, (uint)D2D1_GAUSSIANBLUR_PROP.D2D1_GAUSSIANBLUR_PROP_BORDER_MODE, (uint)D2D1_BORDER_MODE.D2D1_BORDER_MODE_HARD);
                                                        pEffect.GetOutput(out pOutputImage);                                           
                                                    }
                                                }
                                                if (cbGrayscale.IsChecked == true)
                                                {                                                  
                                                    hr = pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Grayscale, out pEffect);
                                                    if (SUCCEEDED(hr))
                                                    {
                                                        pEffect.SetInput(0, pD2DBitmap);                                                      
                                                        pEffect.GetOutput(out pOutputImage);                                
                                                    }                                                  
                                                }
                                                if (cbInvert.IsChecked == true)
                                                {
                                                    hr = pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Invert, out pEffect);
                                                    if (SUCCEEDED(hr))
                                                    {
                                                        pEffect.SetInput(0, pD2DBitmap);
                                                        pEffect.GetOutput(out pOutputImage);
                                                    }
                                                }

                                                pD2DBitmapNew = ConvertD2DImageToD2DBitmap(pD2DDeviceContext, pOutputImage);
                                                if (pD2DBitmapNew != null)
                                                {
                                                    //var sourceRectBackground = new D2D1_RECT_F(0.0f, 0.0f, textureDesc.Width, textureDesc.Height);
                                                    var sourceRectBackground = new D2D1_RECT_F(0.0f, 0.0f, (float)destRect.Width, (float)destRect.Height);
                                                    var destRectBackgroundNormal = new D2D1_RECT_F(0.0f, 0.0f, (float)destRect.Width, (float)destRect.Height);
                                                    pD2DDeviceContext.DrawBitmap(pD2DBitmapNew, ref destRectBackgroundNormal, 1.0f, D2D1_BITMAP_INTERPOLATION_MODE.D2D1_BITMAP_INTERPOLATION_MODE_LINEAR, ref sourceRectBackground);
                                                    ////pD2DDeviceContext.EndDraw( out ulong tag1,out ulong tag2);
                                                    SafeRelease(ref pD2DBitmapNew);
                                                }
                                                SafeRelease(ref pOutputImage);
                                                SafeRelease(ref pEffect);
                                                SafeRelease(ref pD2DBitmap);                                               
                                            }
                                            SafeRelease(ref pD2DDeviceContext);
                                            m_pSurfaceInterop.EndDraw();
                                        }
                                        SafeRelease(ref pDXGISurfaceTexture);                                        
                                        SafeRelease(ref pOriginalTexture);
                                        SafeRelease(ref pD3D11Device);
                                        Marshal.Release(pDevicePtr);
                                    }
                                }                                                             
                            }
                            else
                            {
                                m_currentMediaPlayer.CopyFrameToVideoSurface(pDirect3DSurface, destRect);
                                m_pSurfaceInterop.EndDraw();
                            }                            
                            Marshal.Release(pGraphicsSurfacePtr);
                            pDirect3DSurface.Dispose();
                        }
                        SafeRelease(ref pDXGISurface);
                        Marshal.Release(pDXGISurfacePtr);                        

                        //hr = m_pSurfaceInterop.EndDraw();
                    }
                }
                m_bFrame = false;
            }
            return hr;
        }

        private void OnVideoFrameAvailable(MediaPlayer sender, object args)
        {
            lock (_surfaceLock)
            {
                m_currentMediaPlayer = sender;
                m_bFrame = true;
            }              
        }

        private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args)
        {
            lock (_surfaceLock)
            {
                //HRESULT hr = HRESULT.S_OK;
                if (m_SpriteVisual != null && m_pSurfaceInterop != null)
                {
                    m_SpriteVisual.Size = new System.Numerics.Vector2((float)args.Size.Width, (float)args.Size.Height);
                    m_SpriteVisual.CenterPoint = new System.Numerics.Vector3((float)args.Size.Width / 2f, (float)args.Size.Height / 2f, 0f);                   

                    // Comment eliminates flickering/bug when too small
                    //SIZE sz = new SIZE((int)args.Size.Width, (int)args.Size.Height);
                    //hr = m_pSurfaceInterop.Resize(sz);
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private float _spriteOpacity = 0.8f;
        public float SpriteOpacity
        {
            get => _spriteOpacity;
            set
            {
                if (_spriteOpacity != value)
                {
                    _spriteOpacity = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SpriteOpacity)));
                    
                    if (m_SpriteVisual != null)                    
                        m_SpriteVisual.Opacity = _spriteOpacity;                   
                }
            }
        }

        private float _spriteRotation = 0f; // in degrees
        public float SpriteRotation
        {
            get => _spriteRotation;
            set
            {
                if (_spriteRotation != value)
                {
                    _spriteRotation = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SpriteRotation)));

                    if (m_SpriteVisual != null)
                    {
                        // Keep rotation around window center
                        //float nWindowWidth = (float)this.AppWindow.ClientSize.Width;
                        //float nWindowHeight = (float)this.AppWindow.ClientSize.Height;
                        //m_SpriteVisual.CenterPoint = new System.Numerics.Vector3(nWindowWidth / 2f, nWindowHeight / 2f, 0f);

                        // Convert degrees to radians
                        float radians = _spriteRotation * (float)Math.PI / 180f;
                        m_SpriteVisual.Orientation = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, radians);
                    }
                }
            }
        }

        private float _strengthEmboss = 5f;
        public float StrengthEmboss
        {
            get => _strengthEmboss;
            set
            {
                if (_strengthEmboss != value)
                {
                    _strengthEmboss = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StrengthEmboss)));                    
                }
            }
        }

        private float _gaussianBlur = 3f;
        public float GaussianBlur
        {
            get => _gaussianBlur;
            set
            {
                if (_gaussianBlur != value)
                {
                    _gaussianBlur = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GaussianBlur)));
                }
            }
        }

        private void cbEmboss_Checked(object sender, RoutedEventArgs e)
        {
            if (cbGrayscale.IsChecked == true)
            {
                cbGrayscale.IsChecked = false;
            }

            if (cbInvert.IsChecked == true)
            {
                cbInvert.IsChecked = false;
            }

            if (cbGaussianBlur.IsChecked == true)
            {
                cbGaussianBlur.IsChecked = false;
            }
        }

        private void cbGaussianBlur_Checked(object sender, RoutedEventArgs e)
        {
            if (cbGrayscale.IsChecked == true)
            {
                cbGrayscale.IsChecked = false;
            }

            if (cbInvert.IsChecked == true)
            {
                cbInvert.IsChecked = false;
            }

            if (cbEmboss.IsChecked == true)
            {
                cbEmboss.IsChecked = false;
            }         
        }

        private void cbGrayscale_Checked(object sender, RoutedEventArgs e)
        {
            if (cbInvert.IsChecked == true)
            {
                cbInvert.IsChecked = false;
            }

            if (cbEmboss.IsChecked == true)
            {
                cbEmboss.IsChecked = false;
            }

            if (cbGaussianBlur.IsChecked == true)
            {
                cbGaussianBlur.IsChecked = false;
            }
        }

        private void cbInvert_Checked(object sender, RoutedEventArgs e)
        {
            if (cbGrayscale.IsChecked == true)
            {
                cbGrayscale.IsChecked = false;
            }

            if (cbEmboss.IsChecked == true)
            {
                cbEmboss.IsChecked = false;
            }

            if (cbGaussianBlur.IsChecked == true)
            {
                cbGaussianBlur.IsChecked = false;
            }
        }

        private void SetEffectFloat(ID2D1Effect pEffect, uint nEffect, float fValue)
        {
            float[] aFloatArray = { fValue };
            int nDataSize = aFloatArray.Length * Marshal.SizeOf(typeof(float));
            IntPtr pData = Marshal.AllocHGlobal(nDataSize);
            Marshal.Copy(aFloatArray, 0, pData, aFloatArray.Length);
            HRESULT hr = pEffect.SetValue(nEffect, D2D1_PROPERTY_TYPE.D2D1_PROPERTY_TYPE_UNKNOWN, pData, (uint)nDataSize);
            Marshal.FreeHGlobal(pData);
        }

        private void SetEffectFloatArray(ID2D1Effect pEffect, uint nEffect, float[] aFloatArray)
        {
            int nDataSize = aFloatArray.Length * Marshal.SizeOf(typeof(float));
            IntPtr pData = Marshal.AllocHGlobal(nDataSize);
            Marshal.Copy(aFloatArray, 0, pData, aFloatArray.Length);
            HRESULT hr = pEffect.SetValue(nEffect, D2D1_PROPERTY_TYPE.D2D1_PROPERTY_TYPE_UNKNOWN, pData, (uint)nDataSize);
            Marshal.FreeHGlobal(pData);
        }

        private void SetEffectInt(ID2D1Effect pEffect, uint nEffect, uint nValue)
        {
            IntPtr pData = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Int32)));
            Marshal.WriteInt32(pData, (int)nValue);
            HRESULT hr = pEffect.SetValue(nEffect, D2D1_PROPERTY_TYPE.D2D1_PROPERTY_TYPE_UNKNOWN, pData, (uint)Marshal.SizeOf(typeof(Int32)));
            Marshal.FreeHGlobal(pData);
        }

        ID2D1Bitmap1? ConvertD2DImageToD2DBitmap(ID2D1DeviceContext pD2DDeviceContext, ID2D1Image pImageSource)
        {              
            HRESULT hr = pD2DDeviceContext.GetImageLocalBounds(pImageSource, out D2D1_RECT_F fBounds);
            if (SUCCEEDED(hr))
            {
                float fWidth = fBounds.right - fBounds.left;
                float fHeight = fBounds.bottom - fBounds.top;
                //uint nWidth = (uint)Math.Ceiling(fWidth);
                //uint nHeight = (uint)Math.Ceiling(fHeight);
                uint nWidth = (uint)fWidth;
                uint nHeight = (uint)fHeight;
                if (nWidth == 0 || nHeight == 0)
                    return null;

                D2D1_BITMAP_PROPERTIES1 bitmapProperties = new D2D1_BITMAP_PROPERTIES1();
                bitmapProperties.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;               
                bitmapProperties.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
                bitmapProperties.dpiX = 96.0f;
                bitmapProperties.dpiY = 96.0f;
                hr = pD2DDeviceContext.CreateBitmap(new D2D1_SIZE_U(nWidth, nHeight), IntPtr.Zero, 0, ref bitmapProperties, out ID2D1Bitmap1 pOutputBitmap);
                if (SUCCEEDED(hr))
                {
                    pD2DDeviceContext.GetTarget(out ID2D1Image pOldTarget);
                    pD2DDeviceContext.SetTarget(pOutputBitmap);

                    //pD2DDeviceContext.BeginDraw();

                    pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black, 1.0f));
                    var destRectBackground = new D2D1_RECT_F(0.0f, 0.0f, fWidth, fHeight);
                    D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
                    pD2DDeviceContext.DrawImage(pImageSource, ref pt, ref destRectBackground, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);

                    //pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);

                    pD2DDeviceContext.SetTarget(pOldTarget);
                    SafeRelease(ref pOldTarget);
                    return pOutputBitmap;
                }
            }
            return null;          
        }

        HRESULT CreateD2D1Factory()
        {
            HRESULT hr = HRESULT.S_OK;
            D2D1_FACTORY_OPTIONS options = new D2D1_FACTORY_OPTIONS();

            // Needs "Enable native code debugging"
#if DEBUG
            options.debugLevel = D2D1_DEBUG_LEVEL.D2D1_DEBUG_LEVEL_INFORMATION;
#endif

            hr = D2DTools.D2D1CreateFactory(D2D1_FACTORY_TYPE.D2D1_FACTORY_TYPE_SINGLE_THREADED, ref D2DTools.CLSID_D2D1Factory, ref options, out m_pD2DFactory);
            m_pD2DFactory1 = (ID2D1Factory1)m_pD2DFactory;
            return hr;
        }

        public HRESULT CreateDeviceContext()
        {
            HRESULT hr = HRESULT.S_OK;
            uint creationFlags = (uint)D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT;

            // Needs "Enable native code debugging"
#if DEBUG
            creationFlags |= (uint)D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_DEBUG;
#endif

            int[] aD3D_FEATURE_LEVEL = new int[] { (int)D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_1, (int)D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0,
                (int)D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_1, (int)D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_0, (int)D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_3,
                (int)D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_2, (int)D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_1};

            D3D_FEATURE_LEVEL featureLevel;
            hr = D2DTools.D3D11CreateDevice(null,    // specify null to use the default adapter
                D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
                IntPtr.Zero,
                creationFlags,              // optionally set debug and Direct2D compatibility flags
                                            //pD3D_FEATURE_LEVEL,              // list of feature levels this app can support
                aD3D_FEATURE_LEVEL,
                //(uint)Marshal.SizeOf(aD3D_FEATURE_LEVEL),   // number of possible feature levels
                (uint)aD3D_FEATURE_LEVEL.Length,
                D2DTools.D3D11_SDK_VERSION,
                out m_pD3D11DevicePtr,                    // returns the Direct3D device created
                out featureLevel,            // returns feature level of device created
                                             //out pD3D11DeviceContextPtr                    // returns the device immediate context
                out m_pD3D11DeviceContext
            );
            if (SUCCEEDED(hr))
            {
                //m_pD3D11DeviceContext = Marshal.GetObjectForIUnknown(pD3D11DeviceContextPtr) as ID3D11DeviceContext;             

                //ID2D1Multithread m_D2DMultithread;
                //m_D2DMultithread = (ID2D1Multithread)m_pD2DFactory1;

                //m_pD2DFactory1.GetDesktopDpi(out float x, out float y);

                m_pDXGIDevice = Marshal.GetObjectForIUnknown(m_pD3D11DevicePtr) as IDXGIDevice1;
                if (m_pD2DFactory1 != null)
                {
                    hr = m_pD2DFactory1.CreateDevice(m_pDXGIDevice, out m_pD2DDevice);
                    if (SUCCEEDED(hr))
                    {
                        hr = m_pD2DDevice.CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS.D2D1_DEVICE_CONTEXT_OPTIONS_NONE, out m_pD2DDeviceContext);
                        //SafeRelease(ref pD2DDevice);
                    }
                }
                //Marshal.Release(m_pD3D11DevicePtr);
            }
            return hr;
        }       

        void Clean()
        {
            m_SpriteVisual?.Dispose();
            SafeRelease(ref m_pSurfaceInterop);            

            SafeRelease(ref m_pD2DDeviceContext);
            SafeRelease(ref m_pD2DDevice);         

            SafeRelease(ref m_pDXGIDevice);
            SafeRelease(ref m_pD3D11DeviceContext);
            if (m_pD3D11DevicePtr != IntPtr.Zero)
                Marshal.Release(m_pD3D11DevicePtr);
            SafeRelease(ref m_pSharedTexture);

            SafeRelease(ref m_pD2DFactory1);
            SafeRelease(ref m_pD2DFactory);
            
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // Warnings from CreateDirect3D11SurfaceFromDXGISurface
            // to be fixed...

            /*
             D3D11 WARNING: Live ID3D11Device at 0x000003063CBA4D40, Refcount: 122 [ STATE_CREATION WARNING #441: LIVE_DEVICE]
 D3D11 WARNING: 	Live ID3D11Context at 0x000003063CBA6D70, Refcount: 6, IntRef: 1 [ STATE_CREATION WARNING #2097226: LIVE_CONTEXT]
 D3D11 WARNING: 	Live ID3DDeviceContextState at 0x000003063CBE5B20, Refcount: 0, IntRef: 1 [ STATE_CREATION WARNING #3145742: LIVE_DEVICECONTEXTSTATE]
 D3D11 WARNING: 	Live ID3D11BlendState at 0x000003063C9F51A0, Refcount: 1, IntRef: 1 [ STATE_CREATION WARNING #435: LIVE_BLENDSTATE]
            ...
             */

#if DEBUG           
            if (DXGIGetDebugInterface1(0, typeof(IDXGIDebug1).GUID, out object debug) == HRESULT.S_OK)
            {
                ((IDXGIDebug1)debug).ReportLiveObjects(DXGI_DEBUG_ALL, DXGI_DEBUG_RLO_FLAGS.DXGI_DEBUG_RLO_SUMMARY | DXGI_DEBUG_RLO_FLAGS.DXGI_DEBUG_RLO_DETAIL);
                SafeRelease(ref debug);
            }
#endif
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {           
            if (m_mediaPlayer != null)
            {
                m_mediaPlayer.VideoFrameAvailable -= OnVideoFrameAvailable;
                m_mediaPlayer.Pause();
                m_mediaPlayer.Source = null;
                m_mediaPlayer.Dispose();
                m_mediaPlayer = null;
            }
            Clean();
        }       
    }
}
