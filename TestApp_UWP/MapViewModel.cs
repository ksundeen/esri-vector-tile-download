using System;
using System.IO;
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
using Windows.UI.Xaml;
using Windows.ApplicationModel.Core;
//using Microsoft.UI.Xaml.Controls;

namespace TestApp_UWP
{
    /// <summary>
    /// Provides map data to an application
    /// </summary>
    public class MapViewModel : INotifyPropertyChanged
    {
        /*** Find other Esri Vector Tile Layers (REST endpoint of /VectorTileServer) basemaps: https://www.arcgis.com/home/group.html?id=c61ab1493fff4b84b53705184876c9b0 ***/
        private static string _defaultVectorTileId = "de26a3cf4cc9451298ea173c4b324736";      // World Street Map, vector tile layer, https://www.arcgis.com/home/item.html?id=de26a3cf4cc9451298ea173c4b324736
        private static string _topoVectorTileId = "7dc6cea0b1764a1f9af2e679f642f0f5";         //World Topographic Map, vector tile layer, https://www.arcgis.com/home/item.html?id=7dc6cea0b1764a1f9af2e679f642f0f5
        private static string _satelliteVectorTileId = "898f58f2ee824b3c97bae0698563a4b3";    // Worl Imagery (WGS84) vector lile layer, https://www.arcgis.com/home/item.html?id=898f58f2ee824b3c97bae0698563a4b3

        private static Uri _vectorTileUri = new Uri("https://basemaps.arcgis.com/arcgis/rest/services/World_Basemap_Export_v2/VectorTileServer");
        private static Uri _rasterTileUri = new Uri("https://tiledbasemaps.arcgis.com/arcgis/rest/services/World_Street_Map/MapServer");
        //private Uri _serviceUri = new Uri("https://sampleserver6.arcgisonline.com/arcgis/rest/services/World_Street_Map/MapServer");

        /*** Find other Tiled Layers (REST endpoint of /MapServer) https://www.arcgis.com/home/group.html?id=3a890be7a4b046c7840dc4a0446c5b31&view=list#content  ***/

        /*** Offline WebMaps ***/
        // Find sample webmap basemaps: https://www.arcgis.com/home/group.html?id=30de8da907d240a0bccd5ad3ff25ef4a&view=list#content
        
        // Store server tasks
        private GenerateOfflineMapJob _generateOfflineMapJob;
        private ExportVectorTilesJob _exportVectorTilesJob;
        private ExportTileCacheJob _exportTileCacheJob;
        private string _packagePath;
        private GenerateOfflineMapResult _offlineMapResult;

        private static string vectorWebMapId = "55ebf90799fa4a3fa57562700a68c405"; // https://www.arcgis.com/home/item.html?id=55ebf90799fa4a3fa57562700a68c405

        #region Compression & Scale parameters
        private int _minLevelOfDetails = 2; 
        private int _maxLevelOfDetails = 20;

        // The higher # is higher quality. To reduce size, reduce #.
        private double _compressionQuality = 100;

        // Buffer to apply to box extent to test different sizes
        private bool _applyBuffer = true;
        private int _bufferDistance = 400;

        // Path to exported tile cache.
        private string _tilePath;
        public string TilePath { 
            get { return _tilePath; } 
            set { _tilePath = value; OnPropertyChanged(); } 
        }

        #endregion

        #region Properties
        public ICommand DownloadOfflineMapCommand { get; private set; }
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

        #endregion

        // Constructor
        public MapViewModel()
        {
            DownloadOfflineMapCommand = new Command(async () => await StartOfflineMapDownloadAsync());
            DownloadVectorTilesCommand = new Command(async () => await StartVectorTileDownloadAsync());
            DownloadRasterTilesCommand = new Command(async () => await StartRasterTileDownloadAsync());
            SetupMap();
        }


        // Load arcgis.com to grab default basemap vector tiles for download
        private async void SetupMap()
        {
            // Set viewpoint at Naperville
            Map = new Map(BasemapType.Streets, 41.7691511, -88.1528445, 15)
            {
                MaxScale = 2000,    // max 1/2000 scale to zoom in
                MinScale = 10000000 // minimum 1/10000000 scale to zoom out
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
            if (_applyBuffer)
            {
                // Expand the area of interest based on the specified buffer distance.
                OfflineArea = GeometryEngine.BufferGeodetic(OfflineArea, _bufferDistance, LinearUnits.Meters).Extent;
            }
            

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

            // Create a portal. If a URI is not specified, www.arcgis.com is used by default.
            Portal = await ArcGISPortal.CreateAsync();
        }

        #region Offline Map Downloads
        public async Task StartOfflineMapDownloadAsync()
        {
            try
            {
                // Get a web map item using its ID.
                PortalItem webmapItem = await PortalItem.CreateAsync(Portal, vectorWebMapId);

                // Create a map from the web map item & set current map
                Map onlineMap = new Map(webmapItem);
                Map = onlineMap;

                ShowStatusMessage("Download started...", "Offline Map");

                // Create a new folder for the output mobile map.
                _packagePath = CreateDownloadPackagePath(DownloadMapType.OfflineMap);

                try
                {
                    // Create an offline map task with the current (online) map.
                    OfflineMapTask takeMapOfflineTask = await OfflineMapTask.CreateAsync(webmapItem);

                    GenerateOfflineMapParameters parameters = await takeMapOfflineTask.CreateDefaultGenerateOfflineMapParametersAsync(OfflineArea);
                    parameters.EsriVectorTilesDownloadOption = EsriVectorTilesDownloadOption.UseReducedFontsService;
                    parameters.MaxScale = 500;
                    parameters.MinScale = 10000000;
                    parameters.IncludeBasemap = true;
                    parameters.AreaOfInterest = OfflineArea;

                    /*************************/
                    /*************************/
                    /********** TODO *********/
                    //parameters.ReferenceBasemapDirectory = Set location of existing data on device
                    //parameters.ReferenceBasemapFilename = Set filename of basemap to use on device
                    /*************************/
                    /*************************/

                    // Check offline capabilities
                    CheckOfflineCapabilities(takeMapOfflineTask, parameters);
                    #region overrides

                    // Generate parameter overrides for more in-depth control of the job.
                    GenerateOfflineMapParameterOverrides overrides = await takeMapOfflineTask.CreateGenerateOfflineMapParameterOverridesAsync(parameters);

                    // Configure the overrides using helper methods.
                    ConfigureTileLayerOverrides(overrides);

                    // Create the job with the parameters and output location.
                    _generateOfflineMapJob = takeMapOfflineTask.GenerateOfflineMap(parameters, _packagePath, overrides);

                    #endregion overrides

                    // Handle the progress changed event for the job.
                    _generateOfflineMapJob.ProgressChanged += OfflineMapJob_ProgressChanged;
                    //_generateOfflineMapJob.JobChanged += OfflineMapJob_JobChanged;
                    ProcessOfflineMapJobResults(_generateOfflineMapJob);

                }
                catch (TaskCanceledException)
                {
                    // Generate offline map task was canceled.
                    ShowStatusMessage("Taking map offline was canceled", "Cancelled");
                }
                catch (Exception ex)
                {
                    // Exception while taking the map offline.
                    ShowStatusMessage(ex.Message, "Offline map error");
                }
            }
            catch (Exception)
            {

                throw;
            }
        }
        private async void CheckOfflineCapabilities(OfflineMapTask task, GenerateOfflineMapParameters parameters)
        {
            OfflineMapCapabilities results = await task.GetOfflineMapCapabilitiesAsync(parameters);
            if (results.HasErrors)
            {
                // Handle possible errors with layers
                foreach (var layerCapability in results.LayerCapabilities)
                {
                    if (!layerCapability.Value.SupportsOffline)
                    {
                        ShowStatusMessage(layerCapability.Key.Name + " cannot be taken offline. Error : " + layerCapability.Value.Error.Message, "Offline Map Layer Error");
                    }
                }

                // Handle possible errors with tables
                foreach (var tableCapability in results.TableCapabilities)
                {
                    if (!tableCapability.Value.SupportsOffline)
                    {
                        ShowStatusMessage(tableCapability.Key.TableName + " cannot be taken offline. Error : " + tableCapability.Value.Error.Message, "Table Error");
                    }
                }
            }
            else
            {
                // All layers and tables can be taken offline!
                ShowStatusMessage("All layers can be exported", "Status");
            }
        }
        private async void ProcessOfflineMapJobResults(GenerateOfflineMapJob job)
        {
            _offlineMapResult = await _generateOfflineMapJob.GetResultAsync();

            var dispatcher = CoreApplication.MainView.CoreWindow.Dispatcher;

            // Check for job failure (writing the output was denied, e.g.).
            if (job.Status == JobStatus.Succeeded)
            {
                await dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    string msg = "Download Succeeded. See downloaded package in: " + _packagePath;
                    ShowStatusMessage(msg, "Offline Map Download Completed");
                });
                // Display the offline map.
                Map = _offlineMapResult.OfflineMap;
            } else if (_generateOfflineMapJob.Status == JobStatus.Failed)
            {
                await dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    ShowStatusMessage("Download failed.", "Offline Map Download");
                    Debug.WriteLine("Vector Tile status: " + job.Status);
                });
            }
            // Check for errors with individual layers.
            if (_offlineMapResult.LayerErrors.Any())
            {
                // Build a string to show all layer errors.
                StringBuilder errorBuilder = new StringBuilder();
                foreach (KeyValuePair<Layer, Exception> layerError in _offlineMapResult.LayerErrors)
                {
                    errorBuilder.AppendLine(string.Format("{0} : {1}", layerError.Key.Id, layerError.Value.Message));
                }

                // Show layer errors.
                await dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    string errorText = errorBuilder.ToString();
                    ShowStatusMessage(errorText, "Layer errors");
                });
            }
        }
        private void ConfigureTileLayerOverrides(GenerateOfflineMapParameterOverrides overrides)
        {
            // Create a parameter key for the first basemap layer. Type is Layer (can be FeatureLayer, ArcGISTiledLayer, or ArcGISVectorTiledLayer
            ArcGISVectorTiledLayer vectorLayer = new ArcGISVectorTiledLayer(_vectorTileUri);
            ArcGISTiledLayer rasterLayer = new ArcGISTiledLayer(_rasterTileUri);
            // Add basemap to layers
            Map.Basemap.BaseLayers.Add(vectorLayer);
            Map.Basemap.BaseLayers.Add(rasterLayer);

            OfflineMapParametersKey basemapTileCacheKey = new OfflineMapParametersKey(Map.Basemap.BaseLayers.ElementAt(1));
            ExportTileCacheParameters basemapTileCacheParameters = new ExportTileCacheParameters();
            // Get the export tile cache parameters for the layer key.
            //ExportTileCacheParameters basemapTileCacheParams = overrides.ExportTileCacheParameters[basemapTileCacheKey];

            // Set the highest possible export quality.
            basemapTileCacheParameters.CompressionQuality = 100;

            // Clear the existing level IDs.
            basemapTileCacheParameters.LevelIds.Clear();

            // Get the min and max scale from the UI.
            int minLevel = _minLevelOfDetails; //5;
            int maxLevel = _maxLevelOfDetails; // 15;

            // Re-add selected scales.
            for (int i = minLevel; i < maxLevel; i++)
            {
                basemapTileCacheParameters.LevelIds.Add(i);
            }

            // Add new overides associated with tile cache basemaps
            overrides.ExportTileCacheParameters.Add(basemapTileCacheKey, basemapTileCacheParameters);

            // Configure VectorTile overrides
            OfflineMapParametersKey basemapVectorTileKey = new OfflineMapParametersKey(Map.Basemap.BaseLayers.ElementAt(0));
            //ExportVectorTilesParameters basemapVectorTileParameters = overrides.ExportVectorTilesParameters[basemapTileCacheKey];
            ExportVectorTilesParameters basemapVectorTileParameters = new ExportVectorTilesParameters();

            basemapVectorTileParameters.MaxLevel = 14;

            // Expand the area of interest based on the specified buffer distance.
            basemapVectorTileParameters.AreaOfInterest = OfflineArea;
        }
        private async void OfflineMapJob_ProgressChanged(object sender, EventArgs e)
        {
            // Get the job.
            GenerateOfflineMapJob job = sender as GenerateOfflineMapJob;
            var dispatcher = CoreApplication.MainView.CoreWindow.Dispatcher;
            await dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                string percentageComplete = job.Progress > 0 ? job.Progress.ToString() + " %" : string.Empty;
                Debug.WriteLine($"Progress: {percentageComplete}%", "Export Process");
            });
        }
        #endregion

        #region Vector Tile Cache Downloads
        public async Task StartVectorTileDownloadAsync()
        {
            try
            {
                // Get the portal item for a web map using its unique item id.
                VectorTileItem = await PortalItem.CreateAsync(Portal, _defaultVectorTileId); // DefaultVectorTileId);

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
                    var dispatcher = CoreApplication.MainView.CoreWindow.Dispatcher;
                    await dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        ShowStatusMessage("This map HAS more resources to download", "Vector Resources");
                    });
                }
                else
                {
                    Debug.WriteLine("No style resources");
                    var dispatcher = CoreApplication.MainView.CoreWindow.Dispatcher;
                    await dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        ShowStatusMessage("This map DOES NOT have more resources to download", "Vector Resources");
                    });
                }

                // Create a new folder for the output map.
                string packagePath = CreateDownloadPackagePath(DownloadMapType.VectorTile);
                TilePath = Path.Combine(packagePath, "VectorTiles.vtpk");  

                // Create the job from the parameters and path to the local cache.
                _exportVectorTilesJob = exportVectorTileTask.ExportVectorTiles(exportVectorTileParams, TilePath);
                // Handle the progress changed event for the job.
                _exportVectorTilesJob.ProgressChanged += ExportVectorTilesJob_ProgressChanged;

                // Handle job status change to check the status.
                _exportVectorTilesJob.JobChanged += ExportVectorTilesJob_JobChanged; 
                
                // Start the job.
                ShowStatusMessage("Vector download started...", "Status");
                _exportVectorTilesJob.Start();

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed: {ex.ToString()}");
                ShowStatusMessage(ex.ToString(), "Error");
            }
        }

        private async void ExportVectorTilesJob_ProgressChanged(object sender, EventArgs e)
        {
            var dispatcher = CoreApplication.MainView.CoreWindow.Dispatcher;

            // Get the job.
            ExportVectorTilesJob job = sender as ExportVectorTilesJob;
            dispatcher = CoreApplication.MainView.CoreWindow.Dispatcher;

            // Show job status and progress.
            await dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                string percentageComplete = job.Progress > 0 ? job.Progress.ToString() + " %" : string.Empty;
                Debug.WriteLine($"Progress: {percentageComplete}%", "Export Process");
            });
        }

        private async void ExportVectorTilesJob_JobChanged(object sender, EventArgs e)
        {
            // Get the job.
            ExportVectorTilesJob job = sender as ExportVectorTilesJob;
            var dispatcher = CoreApplication.MainView.CoreWindow.Dispatcher;

            // When the job succeeds, display the local vector tiles.
            if (job.Status == JobStatus.Succeeded)
            {
                // Get the result from the job.
                ExportVectorTilesResult result = await job.GetResultAsync();

                // Create a vector tile cache from the result.
                VectorTileCache vectorCache = result.VectorTileCache;

                // Create new vector tiled layer using the tile cache.
                ArcGISVectorTiledLayer localVectorTileLayer = new ArcGISVectorTiledLayer(vectorCache);

                // Display the layer as a basemap.
                Map = new Map(new Basemap(localVectorTileLayer));

                await dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    string msg = "Download Succeeded. See downloaded package in: " + _packagePath;
                    ShowStatusMessage(msg, "Offline Map Download Completed");
                });

            }
            else if (_exportVectorTilesJob.Status == JobStatus.Failed)
            {
                await dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    ShowStatusMessage("Vector Tile Export Failed: " + job.Status, "Vector Tile Export Failed");
                });
            }
            else
            {
                Debug.WriteLine("Vector Tile status: " + job.Status);
            }
        }
        #endregion

        #region Raster Tile Cache Download
        /// <summary>
        /// Code modified from https://developers.arcgis.com/net/uwp/sample-code/export-tiles/
        /// </summary>
        /// <returns></returns>
        public async Task StartRasterTileDownloadAsync()
        {
            try
            {
                // Add basemap to layers
                ArcGISTiledLayer imageLayer = new ArcGISTiledLayer(_rasterTileUri);
                Map.Basemap.BaseLayers.Add(imageLayer);
                ShowStatusMessage("Raster download started...", "Status");

                // Update the tile cache path.
                // Create a new folder for the output map.
                _packagePath = CreateDownloadPackagePath(DownloadMapType.RasterTile);
                TilePath = Path.Combine(_packagePath, "m_RasterTiles.tpk");

                // Get the parameters for the job.
                ExportTileCacheParameters parameters = GetExportParameters();

                // Create the task.
                ExportTileCacheTask exportTask = await ExportTileCacheTask.CreateAsync(_rasterTileUri);

                // Create the export job.
                _exportTileCacheJob = exportTask.ExportTileCache(parameters, TilePath);

                // Start the export job.
                _exportTileCacheJob.Start();
                _exportTileCacheJob.ProgressChanged += ExportTileCacheJob_ProgressChanged;

                // Handle job status change to check the status.
                _exportTileCacheJob.JobChanged += ExportTileCacheJob_JobChanged;

            }
            catch (Exception ex)
            {
                ShowStatusMessage(ex.ToString(), "Error");
            }
        }
        private async void ExportTileCacheJob_JobChanged(object sender, EventArgs e)
        {
            // Get the job.
            ExportTileCacheJob job = sender as ExportTileCacheJob;
            var dispatcher = CoreApplication.MainView.CoreWindow.Dispatcher;

            // When the job succeeds, display the local vector tiles.
            if (job.Status == JobStatus.Succeeded)
            {

                // Get the tile cache result.
                TileCache tileCache = await _exportTileCacheJob.GetResultAsync();

                // Create new vector tiled layer using the tile cache.
                ArcGISTiledLayer localTiledLayer = new ArcGISTiledLayer(tileCache);

                // Display the layer as a basemap.
                Map = new Map(new Basemap(localTiledLayer));
                await dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    string msg = "Output location: " + TilePath;
                    ShowStatusMessage(msg, "Raster Tile Package");
                });

            }
            else if (job.Status == JobStatus.Failed)
            {
                await dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    ShowStatusMessage("Raster tile export failed: " + job.Status, "Raster Tile Export Failed");
                });
            }
        }

        // Show changes in job progress.
        private async void ExportTileCacheJob_ProgressChanged(object sender, EventArgs e)
        {
            // Get the job.
            ExportTileCacheJob job = sender as ExportTileCacheJob;

            var dispatcher = CoreApplication.MainView.CoreWindow.Dispatcher;
            await dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                string percentageComplete = job.Progress > 0 ? job.Progress.ToString() + " %" : string.Empty;
                Debug.WriteLine($"Progress: {percentageComplete}%", "Export Process");
            });
        }

        private ExportTileCacheParameters GetExportParameters()
        {
            // Create a new parameters instance.
            ExportTileCacheParameters parameters = new ExportTileCacheParameters();

            parameters.AreaOfInterest = OfflineArea;

            // Set the highest possible export quality.
            parameters.CompressionQuality = _compressionQuality;

            // Add level IDs.
            //     Note: Failing to add at least one Level ID will result in job failure.
            for (int x = _minLevelOfDetails; x < _maxLevelOfDetails; x++)
            {
                parameters.LevelIds.Add(x);
            }

            // Return the parameters.
            return parameters;
        }
        #endregion

        #region Helpers
        public string CreateDownloadPackagePath(DownloadMapType downloadMapType)
        {
            string folderName = $"{ downloadMapType.ToString()}_buffer{_bufferDistance.ToString()}m";
            // Create a new folder for the output mobile map.
            _packagePath = Path.Combine(Environment.ExpandEnvironmentVariables("%TEMP%"), folderName);
            int num = 1;
            while (Directory.Exists(_packagePath))
            {
                _packagePath = Path.Combine(Environment.ExpandEnvironmentVariables("%TEMP%"), folderName + num.ToString());
                num++;
            }

            // Create the output directory.
            Directory.CreateDirectory(_packagePath);
            return _packagePath;
        }
        private async void ShowStatusMessage(string message, string title)
        {
            // Display the message to the user.
            await new MessageDialog(message, title).ShowAsync();
        }

        public enum DownloadMapType
        {
            VectorTile,
            RasterTile,
            OfflineMap
        }
        #endregion

    }
}
