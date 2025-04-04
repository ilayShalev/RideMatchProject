﻿// Fix RideMatchSchedulerService.cs to properly handle Windows Service events
// and to have better error handling

using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Timers;
using System.Threading.Tasks;
using System.IO;
using System.Configuration;
using claudpro.Models;
using claudpro.Services;

namespace RideMatchScheduler
{
    public class RideMatchSchedulerService : ServiceBase
    {
        private Timer schedulerTimer;
        private DatabaseService dbService;
        private MapService mapService;
        private string logFilePath;
        private bool isRunningTask = false;

        public RideMatchSchedulerService()
        {
            InitializeComponent();

            // Initialize services
            string dbPath = ConfigurationManager.AppSettings["DatabasePath"] ?? "ridematch.db";

            // Make sure it's an absolute path
            if (!Path.IsPathRooted(dbPath))
            {
                // Use service location as base path
                dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dbPath);
            }

            string apiKey = ConfigurationManager.AppSettings["GoogleApiKey"] ?? "";

            // Set up logging path
            logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RideMatchScheduler.log");

            // Create database directory if it doesn't exist
            string dbDirectory = Path.GetDirectoryName(dbPath);
            if (!Directory.Exists(dbDirectory) && !string.IsNullOrEmpty(dbDirectory))
            {
                Directory.CreateDirectory(dbDirectory);
            }

            try
            {
                dbService = new DatabaseService(dbPath);
                mapService = new MapService(apiKey);
            }
            catch (Exception ex)
            {
                Log($"Error initializing services: {ex.Message}");
                throw; // Re-throw to prevent service from starting with initialization errors
            }

            // Set service name
            ServiceName = "RideMatchSchedulerService";
        }

        private void InitializeComponent()
        {
            // Service component initialization
            this.CanStop = true;
            this.CanPauseAndContinue = true;  // Support pause/continue
            this.CanShutdown = true;          // Proper shutdown handling
            this.AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            Log("RideMatch Scheduler Service started");

            try
            {
                // Set timer to check every minute if it's time to run the algorithm
                schedulerTimer = new Timer(60000); // 60,000 ms = 1 minute
                schedulerTimer.Elapsed += CheckScheduleTime;
                schedulerTimer.Start();

                // Load initial settings
                Task.Run(async () => {
                    try
                    {
                        var settings = await dbService.GetSchedulingSettingsAsync();
                        Log($"Service started. Scheduling is {(settings.IsEnabled ? "enabled" : "disabled")}. " +
                            $"Scheduled time: {settings.ScheduledTime.ToString("HH:mm:ss")}");
                    }
                    catch (Exception ex)
                    {
                        Log($"Error loading settings: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Log($"Error in OnStart: {ex.Message}");
                throw; // Re-throw to notify SCM of startup failure
            }
        }

        protected override void OnStop()
        {
            Log("RideMatch Scheduler Service stopping");

            try
            {
                schedulerTimer?.Stop();
                schedulerTimer?.Dispose();

                // Wait for any running task to complete
                int waitCount = 0;
                while (isRunningTask && waitCount < 30) // Wait up to 30 seconds
                {
                    System.Threading.Thread.Sleep(1000);
                    waitCount++;
                    Log($"Waiting for running task to complete... ({waitCount}/30)");
                }

                dbService?.Dispose();
                Log("RideMatch Scheduler Service stopped");
            }
            catch (Exception ex)
            {
                Log($"Error in OnStop: {ex.Message}");
            }
        }

        protected override void OnPause()
        {
            try
            {
                schedulerTimer?.Stop();
                Log("RideMatch Scheduler Service paused");
            }
            catch (Exception ex)
            {
                Log($"Error in OnPause: {ex.Message}");
            }
        }

        protected override void OnContinue()
        {
            try
            {
                schedulerTimer?.Start();
                Log("RideMatch Scheduler Service resumed");
            }
            catch (Exception ex)
            {
                Log($"Error in OnContinue: {ex.Message}");
            }
        }

        protected override void OnShutdown()
        {
            Log("System shutdown detected");

            try
            {
                // Same cleanup as OnStop
                schedulerTimer?.Stop();
                schedulerTimer?.Dispose();
                dbService?.Dispose();
                Log("RideMatch Scheduler Service shutdown complete");
            }
            catch (Exception ex)
            {
                Log($"Error in OnShutdown: {ex.Message}");
            }
        }

        private async void CheckScheduleTime(object sender, ElapsedEventArgs e)
        {
            if (isRunningTask) return; // Prevent multiple runs at once

            try
            {
                isRunningTask = true;

                // Get current time
                DateTime now = DateTime.Now;

                // Get the scheduled time from database
                var settings = await dbService.GetSchedulingSettingsAsync();

                // Check if scheduling is enabled and if it's the correct time
                if (settings.IsEnabled &&
                    now.Hour == settings.ScheduledTime.Hour &&
                    now.Minute == settings.ScheduledTime.Minute)
                {
                    Log($"Running scheduled route calculation at {now}");
                    await RunAlgorithmAsync();
                }
            }
            catch (Exception ex)
            {
                Log($"Error in scheduler: {ex.Message}");
            }
            finally
            {
                isRunningTask = false;
            }
        }

        private async Task RunAlgorithmAsync()
        {
            try
            {
                // Get destination information
                var destination = await dbService.GetDestinationAsync();
                Log($"Using destination: {destination.Name}, Location: {destination.Latitude}, {destination.Longitude}");

                // Get available vehicles and passengers
                var vehicles = await dbService.GetAvailableVehiclesAsync();
                var passengers = await dbService.GetAvailablePassengersAsync();

                // Only run if there are passengers and vehicles
                if (passengers.Count > 0 && vehicles.Count > 0)
                {
                    Log($"Running algorithm with {passengers.Count} passengers and {vehicles.Count} vehicles");

                    // Create a routing service
                    var routingService = new RoutingService(mapService, destination.Latitude, destination.Longitude);

                    // Create the solver
                    var solver = new RideSharingGenetic(
                        passengers,
                        vehicles,
                        200, // Population size
                        destination.Latitude,
                        destination.Longitude,
                        GetTargetTimeInMinutes(destination.TargetTime)
                    );

                    // Run the algorithm
                    var solution = solver.Solve(150); // Generations

                    if (solution != null)
                    {
                        // Calculate route details
                        routingService.CalculateEstimatedRouteDetails(solution);

                        // Save the solution to database for tomorrow's date
                        string tomorrowDate = DateTime.Now.AddDays(1).ToString("yyyy-MM-dd");
                        int routeId = await dbService.SaveSolutionAsync(solution, tomorrowDate);

                        Log($"Algorithm completed and saved as route #{routeId}");
                        Log($"Assigned {solution.Vehicles.Sum(v => v.AssignedPassengers?.Count ?? 0)} passengers to {solution.Vehicles.Count(v => v.AssignedPassengers?.Count > 0)} vehicles");

                        // TODO: Add code here to send notifications to users
                    }
                    else
                    {
                        Log("Algorithm failed to find a valid solution");
                    }
                }
                else
                {
                    Log("No passengers or vehicles available for tomorrow - skipping algorithm run");
                }
            }
            catch (Exception ex)
            {
                Log($"Error running algorithm: {ex.Message}");
                Log($"Stack trace: {ex.StackTrace}");
            }
        }

        private int GetTargetTimeInMinutes(string targetTime)
        {
            // Convert a time string like "08:00:00" to minutes from midnight
            if (TimeSpan.TryParse(targetTime, out TimeSpan time))
            {
                return (int)time.TotalMinutes;
            }

            // Default to 8:00 AM (480 minutes)
            return 480;
        }

        private void Log(string message)
        {
            try
            {
                // Create the directory if it doesn't exist
                string directory = Path.GetDirectoryName(logFilePath);
                if (!Directory.Exists(directory) && !string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Use a lock to prevent multiple threads from writing simultaneously
                lock (this)
                {
                    using (StreamWriter writer = File.AppendText(logFilePath))
                    {
                        writer.WriteLine($"{DateTime.Now}: {message}");
                    }
                }
            }
            catch
            {
                // Logging should never crash the service
                // We can't really log a logging failure...
            }
        }
    }
}