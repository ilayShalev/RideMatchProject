﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


using System.Net.Http;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsForms;
using GMap.NET.WindowsForms.Markers;
using Newtonsoft.Json;
using claudpro.Models;
using claudpro.Utilities;

namespace claudpro.Services
{
    public class MapService
    {
        private readonly string apiKey;
        private readonly HttpClient httpClient;
        private readonly Dictionary<string, List<PointLatLng>> routeCache = new Dictionary<string, List<PointLatLng>>();

        public MapService(string apiKey)
        {
            this.apiKey = apiKey;
            this.httpClient = new HttpClient();
        }

        /// <summary>
        /// Initializes the Google Maps component
        /// </summary>
        public void InitializeGoogleMaps(GMapControl mapControl, double latitude = 40.7128, double longitude = -74.0060)
        {
            GMaps.Instance.Mode = AccessMode.ServerAndCache;
            GoogleMapProvider.Instance.ApiKey = apiKey;

            mapControl.MapProvider = GoogleMapProvider.Instance;
            mapControl.Position = new PointLatLng(latitude, longitude);
            mapControl.MinZoom = 2;
            mapControl.MaxZoom = 18;
            mapControl.Zoom = 12;
            mapControl.DragButton = System.Windows.Forms.MouseButtons.Left;
        }

        /// <summary>
        /// Changes the map provider type
        /// </summary>
        public void ChangeMapProvider(GMapControl mapControl, int providerType)
        {
            switch (providerType)
            {
                case 0: mapControl.MapProvider = GoogleMapProvider.Instance; break;
                case 1: mapControl.MapProvider = GoogleSatelliteMapProvider.Instance; break;
                case 2: mapControl.MapProvider = GoogleHybridMapProvider.Instance; break;
                case 3: mapControl.MapProvider = GoogleTerrainMapProvider.Instance; break;
            }
            mapControl.Refresh();
        }

        /// <summary>
        /// Fetches directions from Google Maps Directions API
        /// </summary>
        public async Task<List<PointLatLng>> GetGoogleDirectionsAsync(List<PointLatLng> waypoints)
        {
            if (waypoints.Count < 2) return null;

            string cacheKey = string.Join("|", waypoints.Select(p => $"{p.Lat},{p.Lng}"));
            if (routeCache.ContainsKey(cacheKey)) return routeCache[cacheKey];

            var origin = waypoints[0];
            var destination = waypoints.Last();
            var intermediates = waypoints.Skip(1).Take(waypoints.Count - 2).ToList();

            string url = $"https://maps.googleapis.com/maps/api/directions/json?" +
                $"origin={origin.Lat},{origin.Lng}&" +
                $"destination={destination.Lat},{destination.Lng}&" +
                (intermediates.Any() ? $"waypoints={string.Join("|", intermediates.Select(p => $"{p.Lat},{p.Lng}"))}&" : "") +
                $"key={apiKey}";

            try
            {
                var response = await httpClient.GetStringAsync(url);
                dynamic data = JsonConvert.DeserializeObject(response);

                if (data.status != "OK") return null;

                var points = new List<PointLatLng>();
                foreach (var leg in data.routes[0].legs)
                    foreach (var step in leg.steps)
                        points.AddRange(PolylineEncoder.Decode(step.polyline.points.ToString()));

                routeCache[cacheKey] = points;
                return points;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Gets route details from the Google Directions API
        /// </summary>
        public async Task<RouteDetails> GetRouteDetailsAsync(Vehicle vehicle, double destinationLat, double destinationLng)
        {
            if (vehicle.AssignedPassengers.Count == 0) return null;

            try
            {
                // Build waypoints
                string origin = $"{vehicle.StartLatitude},{vehicle.StartLongitude}";
                string destination = $"{destinationLat},{destinationLng}";
                string waypointsStr = string.Join("|", vehicle.AssignedPassengers.Select(p => $"{p.Latitude},{p.Longitude}"));

                string url = $"https://maps.googleapis.com/maps/api/directions/json?" +
                    $"origin={origin}" +
                    $"&destination={destination}" +
                    (vehicle.AssignedPassengers.Any() ? $"&waypoints={waypointsStr}" : "") +
                    $"&key={apiKey}";

                var response = await httpClient.GetStringAsync(url);
                dynamic data = JsonConvert.DeserializeObject(response);

                if (data.status.ToString() != "OK")
                {
                    return null;
                }

                var routeDetail = new RouteDetails
                {
                    VehicleId = vehicle.Id,
                    StopDetails = new List<StopDetail>()
                };

                double totalDistance = 0;
                double totalTime = 0;

                // Process legs (segments between consecutive points)
                for (int i = 0; i < data.routes[0].legs.Count; i++)
                {
                    var leg = data.routes[0].legs[i];

                    // Extract information from response
                    double distance = Convert.ToDouble(leg.distance.value) / 1000.0; // Convert meters to km
                    double time = Convert.ToDouble(leg.duration.value) / 60.0; // Convert seconds to minutes

                    totalDistance += distance;
                    totalTime += time;

                    string stopName = i < vehicle.AssignedPassengers.Count
                        ? vehicle.AssignedPassengers[i].Name
                        : "Destination";

                    int passengerId = i < vehicle.AssignedPassengers.Count
                        ? vehicle.AssignedPassengers[i].Id
                        : -1;

                    routeDetail.StopDetails.Add(new StopDetail
                    {
                        StopNumber = i + 1,
                        PassengerId = passengerId,
                        PassengerName = stopName,
                        DistanceFromPrevious = distance,
                        TimeFromPrevious = time,
                        CumulativeDistance = totalDistance,
                        CumulativeTime = totalTime
                    });
                }

                routeDetail.TotalDistance = totalDistance;
                routeDetail.TotalTime = totalTime;

                return routeDetail;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Estimates route details using straight-line distances when Google API is not available
        /// </summary>
        public RouteDetails EstimateRouteDetails(Vehicle vehicle, double destinationLat, double destinationLng)
        {
            if (vehicle.AssignedPassengers.Count == 0) return null;

            var routeDetail = new RouteDetails
            {
                VehicleId = vehicle.Id,
                TotalDistance = 0,
                TotalTime = 0,
                StopDetails = new List<StopDetail>()
            };

            // Calculate time from vehicle start to first passenger
            double currentLat = vehicle.StartLatitude;
            double currentLng = vehicle.StartLongitude;
            double totalDistance = 0;
            double totalTime = 0;

            for (int i = 0; i < vehicle.AssignedPassengers.Count; i++)
            {
                var passenger = vehicle.AssignedPassengers[i];
                double distance = GeoCalculator.CalculateDistance(currentLat, currentLng, passenger.Latitude, passenger.Longitude);
                double time = (distance / 30.0) * 60; // Assuming 30 km/h average speed

                totalDistance += distance;
                totalTime += time;

                routeDetail.StopDetails.Add(new StopDetail
                {
                    StopNumber = i + 1,
                    PassengerId = passenger.Id,
                    PassengerName = passenger.Name,
                    DistanceFromPrevious = distance,
                    TimeFromPrevious = time,
                    CumulativeDistance = totalDistance,
                    CumulativeTime = totalTime
                });

                currentLat = passenger.Latitude;
                currentLng = passenger.Longitude;
            }

            // Calculate trip to final destination
            double distToDest = GeoCalculator.CalculateDistance(currentLat, currentLng, destinationLat, destinationLng);
            double timeToDest = (distToDest / 30.0) * 60;

            totalDistance += distToDest;
            totalTime += timeToDest;

            routeDetail.StopDetails.Add(new StopDetail
            {
                StopNumber = vehicle.AssignedPassengers.Count + 1,
                PassengerId = -1,
                PassengerName = "Destination",
                DistanceFromPrevious = distToDest,
                TimeFromPrevious = timeToDest,
                CumulativeDistance = totalDistance,
                CumulativeTime = totalTime
            });

            routeDetail.TotalDistance = totalDistance;
            routeDetail.TotalTime = totalTime;

            return routeDetail;
        }

        /// <summary>
        /// Gets a color for a route based on the route index
        /// </summary>
        public System.Drawing.Color GetRouteColor(int index)
        {
            System.Drawing.Color[] routeColors = {
                System.Drawing.Color.FromArgb(255, 128, 0),   // Orange
                System.Drawing.Color.FromArgb(128, 0, 128),   // Purple
                System.Drawing.Color.FromArgb(0, 128, 128),   // Teal
                System.Drawing.Color.FromArgb(128, 0, 0),     // Maroon
                System.Drawing.Color.FromArgb(0, 128, 0),     // Green
                System.Drawing.Color.FromArgb(0, 0, 128),     // Navy
                System.Drawing.Color.FromArgb(128, 128, 0),   // Olive
                System.Drawing.Color.FromArgb(128, 0, 64)     // Burgundy
            };
            return routeColors[index % routeColors.Length];
        }

        public void Dispose()
        {
            httpClient?.Dispose();
        }
    }
}