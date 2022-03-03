using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Location;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Security;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.Tasks;
using Esri.ArcGISRuntime.UI;
using Esri.ArcGISRuntime.Portal;
using System.Drawing;
using System.Windows;
using System.Windows.Input;
using Xamarin.Forms;
using Esri.ArcGISRuntime.Tasks.Offline;
using System.Diagnostics;
using Windows.UI.Popups;

namespace TestApp_UWP
{
    /// <summary>
    /// Provides map data to an application
    /// </summary>
    public class MapViewModel : INotifyPropertyChanged
    {
        /*** Find other Esri Vector Tile Layers (REST endpoint of /VectorTileServer) basemaps: https://www.arcgis.com/home/group.html?id=c61ab1493fff4b84b53705184876c9b0 ***/
        public static string DefaultVectorTileId = "de26a3cf4cc9451298ea173c4b324736";      // World Street Map, vector tile layer, https://www.arcgis.com/home/item.html?id=de26a3cf4cc9451298ea173c4b324736
        public static string TopoVectorTileId = "7dc6cea0b1764a1f9af2e679f642f0f5";         //World Topographic Map, vector tile layer, https://www.arcgis.com/home/item.html?id=7dc6cea0b1764a1f9af2e679f642f0f5
        public static string SatelliteVectorTileId = "898f58f2ee824b3c97bae0698563a4b3";    // Worl Imagery (WGS84) vector lile layer, https://www.arcgis.com/home/item.html?id=898f58f2ee824b3c97bae0698563a4b3

        public static Uri VectorTileUri = new Uri("https://basemaps.arcgis.com/arcgis/rest/services/World_Basemap_Export_v2/VectorTileServer");
        public static Uri RasterTileUri = new Uri("https://tiledbasemaps.arcgis.com/arcgis/rest/services/World_Street_Map/MapServer");

        /*** Find other Tiled Layers (REST endpoint of /MapServer) https://www.arcgis.com/home/group.html?id=3a890be7a4b046c7840dc4a0446c5b31&view=list#content  ***/
        public static string TileDownloadType = "RASTER"; // "TILE" or "VECTOR"

        public static string VectorTileId = SatelliteVectorTileId;

        public ICommand DownloadVectorTilesCommand { get; private set; }
        public ICommand DownloadRasterTilesCommand { get; private set; }

        private Map _map;
        /// <summary>
        /// Gets or sets the map
        /// </summary>
        public Map Map
        {
            get { return _map; }
            set { _map = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Raises the <see cref="MapViewModel.PropertyChanged" /> event
        /// </summary>
        /// <param name="propertyName">The name of the property that has changed</param>
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var propertyChangedHandler = PropertyChanged;
            if (propertyChangedHandler != null)
                propertyChangedHandler(this, new PropertyChangedEventArgs(propertyName));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        // Create graphic for download of extent
        private GraphicsOverlayCollection _graphicsOverlays;
        public GraphicsOverlayCollection GraphicsOverlays
        {
            get { return _graphicsOverlays; }
            set
            {
                _graphicsOverlays = value;
                OnPropertyChanged();
            }
        }

        private ArcGISPortal _portal;
        public ArcGISPortal Portal
        {
            get { return _portal; }
            set
            {
                _portal = value;
                OnPropertyChanged();
            }
        }

        private PortalItem _vectorTileItem;
        public PortalItem VectorTileItem
        {
            get { return _vectorTileItem; }
            set
            {
                _vectorTileItem = value;
                OnPropertyChanged();
            }
        }
        
        private Envelope _offlineArea;
        public Envelope OfflineArea
        {
            get { return _offlineArea; }
            set
            {
                _offlineArea = value;
                OnPropertyChanged();
            }
        }

        // Constructor
        public MapViewModel()
        {
            DownloadVectorTilesCommand = new Command(async () => await StartVectorTileDownloadAsync());
            DownloadRasterTilesCommand = new Command(async () => await StartRasterTileDownloadAsync());
            SetupMap();
        }

        // Load arcgis.com to grab default basemap vector tiles for download
        private void SetupMap()
        {
            //this.Map = new Map(Basemap.CreateStreetsVector())
            //{
            //    MaxScale = 5000000,
            //    MinScale = 10000000
            //};

            // Set viewpoint
            Map = new Map(BasemapType.Streets, 41.7527292, -88.2006784, 5)
            {
                MaxScale = 2000,
                MinScale = 10000000
            };

            // Make download envelope
            EnvelopeBuilder envelopeBldr = new EnvelopeBuilder(SpatialReferences.Wgs84)
            {
                XMin = -88.1526,
                XMax = -88.1490,
                YMin = 41.7694,
                YMax = 41.7714
            };

            OfflineArea = envelopeBldr.ToGeometry();

            // Create a graphic to display the area to take offline.
            SimpleLineSymbol lineSymbol = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, Xamarin.Forms.Color.Red, 2);
            SimpleFillSymbol fillSymbol = new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, Xamarin.Forms.Color.Transparent, lineSymbol);
            Graphic offlineAreaGraphic = new Graphic(OfflineArea, fillSymbol);

            // Create a graphics overlay and add the graphic.
            GraphicsOverlay areaOverlay = new GraphicsOverlay();
            areaOverlay.Graphics.Add(offlineAreaGraphic);

            // Add the overlay to a new graphics overlay collection.
            GraphicsOverlayCollection overlays = new GraphicsOverlayCollection
            {
                areaOverlay
            };

            // Set the view model's "GraphicsOverlays" property (will be consumed by the map view).
            this.GraphicsOverlays = overlays;

        }

        public async Task StartVectorTileDownloadAsync()
        {
            try
            {
                // Create a portal. If a URI is not specified, www.arcgis.com is used by default.
                Portal = await ArcGISPortal.CreateAsync();

                // Get the portal item for a web map using its unique item id.
                VectorTileItem = await PortalItem.CreateAsync(Portal, VectorTileId); // DefaultVectorTileId);

                Uri uri = new Uri("https://basemaps.arcgis.com/arcgis/rest/services/World_Basemap_Export_v2/VectorTileServer");
                                 //https://basemaps.arcgis.com/arcgis/rest/services/World_Basemap_v2/VectorTileServer");

                ExportVectorTilesTask exportVectorTileTask = await ExportVectorTilesTask.CreateAsync(uri); // VectorTileItem); //.ServiceUrl);
                // Create the default export vector tile cache job parameters.
                ExportVectorTilesParameters exportVectorTileParams = await exportVectorTileTask.CreateDefaultExportVectorTilesParametersAsync(
                    areaOfInterest: OfflineArea,
                    maxScale: 5000000);

                // Check if the vector tile layer has style resources.
                bool hasStyleResources = exportVectorTileTask.HasStyleResources;

                // Choose whether to download just the style resources or both the styles and the tiles.
                if (hasStyleResources)
                {
                    Debug.WriteLine("Has style resources");
                }
                else
                {
                    Debug.WriteLine("No style resources");
                }

                // Destination path for the local vector cache (.vtpk file).
                string myDocumentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                //string tileCachePath =  System.IO.Path.Combine(myDocumentsFolder, "VectorMapTiles.vtpk"); //"C://output//VectorMapTiles.vtpk"; 
                string tileCachePath = $"{System.IO.Path.GetTempFileName()}.vtpk";  //(myDocumentsFolder, "VectorMapTiles.vtpk"); //"C://output//VectorMapTiles.vtpk"; 

                // Create the job from the parameters and path to the local cache.
                ExportVectorTilesJob exportVectorTilesJob = exportVectorTileTask.ExportVectorTiles(exportVectorTileParams, tileCachePath);
                Debug.WriteLine(exportVectorTilesJob.VectorTileCachePath);

                // Handle job status change to check the status.
                exportVectorTilesJob.JobChanged += async (sender, args) =>
                {
                    Debug.WriteLine("Output location: " + myDocumentsFolder);
                    // Show job status and progress.
                    Debug.WriteLine($"Job status: {exportVectorTilesJob.Status}, progress: {exportVectorTilesJob.Progress}%");

                    // When the job succeeds, display the local vector tiles.
                    if (exportVectorTilesJob.Status == JobStatus.Succeeded)
                    {
                        // Get the result from the job.
                        ExportVectorTilesResult result = await exportVectorTilesJob.GetResultAsync();

                        // Create a vector tile cache from the result.
                        VectorTileCache vectorCache = result.VectorTileCache;

                        // Create new vector tiled layer using the tile cache.
                        ArcGISVectorTiledLayer localVectorTileLayer = new ArcGISVectorTiledLayer(vectorCache);

                        // Display the layer as a basemap.
                        Map = new Map(new Basemap(localVectorTileLayer));

                    }
                    else if (exportVectorTilesJob.Status == JobStatus.Failed)   
                    {
                        Debug.WriteLine("Vector Tile Export Failed: " + exportVectorTilesJob.Status);
                        //string title = "Vector Status";
                        //await new MessageDialog("Download Failed", title).ShowAsync();
                    }
                    else
                    {
                        Debug.WriteLine("Vector Tile status: " + exportVectorTilesJob.Status);
                    }
                };

                // Start the job.
                exportVectorTilesJob.Start();
                
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Failed: " + ex);

            }
        }

        public async Task StartRasterTileDownloadAsync()
        {
            string title = "Under Construction";
            string msg = "Raster Tiles not ready for download yet!";
            // Xamarin Forms
            //await Application.Current.MainPage.DisplayAlert(title, msg, "OK");
            
            // UWP
            await new MessageDialog(msg, title).ShowAsync();
            //try
            //{

            //}
            //catch (Exception)
            //{

            //    throw;
            //}
        }
    }
}
