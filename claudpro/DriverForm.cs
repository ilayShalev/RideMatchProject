﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using GMap.NET;
using GMap.NET.WindowsForms;
using GMap.NET.WindowsForms.Markers;
using claudpro.Models;
using claudpro.Services;
using claudpro.UI;

namespace claudpro
{
    public partial class DriverForm : Form
    {
        // Fields for database and services
        private readonly DatabaseService dbService;
        private readonly MapService mapService;
        private readonly int userId;
        private readonly string username;

        // UI controls
        private GMapControl gMapControl;
        private CheckBox availabilityCheckBox;
        private RichTextBox routeDetailsTextBox;
        private Button refreshButton;
        private Button logoutButton;
        private Panel leftPanel;

        // Fields for location setting functionality
        private bool isSettingLocation = false;
        private Label locationInstructionsLabel;
        private AddressSearchControl addressSearchControl;
        private Button setLocationButton;

        // Data models
        private Vehicle vehicle;
        private List<Passenger> assignedPassengers = new List<Passenger>();
        private DateTime? pickupTime;

        public DriverForm(DatabaseService dbService, MapService mapService, int userId, string username)
        {
            this.dbService = dbService ?? throw new ArgumentNullException(nameof(dbService));
            this.mapService = mapService ?? throw new ArgumentNullException(nameof(mapService));
            this.userId = userId;
            this.username = username ?? throw new ArgumentNullException(nameof(username));

            // Use the designer-generated InitializeComponent
            InitializeComponent();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try
            {
                // Setup UI manually
                SetupUI();

                // Initialize map with default position
                if (gMapControl != null)
                {
                    mapService.InitializeGoogleMaps(gMapControl, 32.0741, 34.7922);
                }

                // Load data asynchronously
                LoadDriverDataAsync().ContinueWith(t =>
                {
                    if (t.IsFaulted && t.Exception != null)
                    {
                        this.Invoke(new Action(() =>
                        {
                            MessageBox.Show($"Error loading driver data: {t.Exception.InnerException?.Message ?? t.Exception.Message}",
                                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }));
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing driver form: {ex.Message}",
                    "Initialization Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SetupUI()
        {
            try
            {
                // Set form properties - this can be moved to the designer
                this.Text = "RideMatch - Driver Interface";
                this.Size = new Size(1000, 700);
                this.FormBorderStyle = FormBorderStyle.FixedDialog;
                this.StartPosition = FormStartPosition.CenterScreen;

                // Title
                var titleLabel = ControlExtensions.CreateLabel(
                    $"Welcome, {username}",
                    new Point(20, 20),
                    new Size(960, 30),
                    new Font("Arial", 16, FontStyle.Bold),
                    ContentAlignment.MiddleCenter
                );
                Controls.Add(titleLabel);

                // Left panel for controls and details
                leftPanel = ControlExtensions.CreatePanel(
                    new Point(20, 70),
                    new Size(350, 580),
                    BorderStyle.FixedSingle
                );
                Controls.Add(leftPanel);

                // Availability controls
                leftPanel.Controls.Add(ControlExtensions.CreateLabel(
                    "Tomorrow's Status:",
                    new Point(20, 20),
                    new Size(150, 20),
                    new Font("Arial", 10, FontStyle.Bold)
                ));

                availabilityCheckBox = ControlExtensions.CreateCheckBox(
                    "I am available to drive tomorrow",
                    new Point(20, 50),
                    new Size(300, 30),
                    true
                );
                availabilityCheckBox.CheckedChanged += async (s, e) => await UpdateAvailabilityAsync();
                leftPanel.Controls.Add(availabilityCheckBox);

                var statusPanel = ControlExtensions.CreatePanel(
                    new Point(20, 90),
                    new Size(310, 2),
                    BorderStyle.FixedSingle
                );
                statusPanel.BackColor = Color.Gray;
                leftPanel.Controls.Add(statusPanel);

                // Route details section
                leftPanel.Controls.Add(ControlExtensions.CreateLabel(
                    "Your Route Details:",
                    new Point(20, 110),
                    new Size(200, 20),
                    new Font("Arial", 10, FontStyle.Bold)
                ));

                routeDetailsTextBox = ControlExtensions.CreateRichTextBox(
                    new Point(20, 140),
                    new Size(310, 200),
                    true
                );
                leftPanel.Controls.Add(routeDetailsTextBox);

                // Add location setting functionality
                AddLocationSettingControls();

                // Buttons
                refreshButton = ControlExtensions.CreateButton(
                    "Refresh",
                    new Point(20, 530),
                    new Size(150, 30),
                    async (s, e) => await LoadDriverDataAsync()
                );
                leftPanel.Controls.Add(refreshButton);

                logoutButton = ControlExtensions.CreateButton(
                    "Logout",
                    new Point(180, 530),
                    new Size(150, 30),
                    (s, e) => Close()
                );
                leftPanel.Controls.Add(logoutButton);

                // Map
                gMapControl = new GMapControl
                {
                    Location = new Point(390, 70),
                    Size = new Size(580, 580),
                    MinZoom = 2,
                    MaxZoom = 18,
                    Zoom = 13,
                    DragButton = MouseButtons.Left
                };
                Controls.Add(gMapControl);

                // Initialize message
                routeDetailsTextBox.AppendText("Loading driver data...\n");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error setting up UI: {ex.Message}",
                    "UI Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AddLocationSettingControls()
        {
            try
            {
                // Add a separator panel
                var locationPanel = ControlExtensions.CreatePanel(
                    new Point(20, 350),
                    new Size(310, 2),
                    BorderStyle.FixedSingle
                );
                locationPanel.BackColor = Color.Gray;
                leftPanel.Controls.Add(locationPanel);

                // Add location setting section title
                leftPanel.Controls.Add(ControlExtensions.CreateLabel(
                    "Set Your Starting Location:",
                    new Point(20, 360),
                    new Size(200, 20),
                    new Font("Arial", 10, FontStyle.Bold)
                ));

                // Add a button for setting location
                setLocationButton = ControlExtensions.CreateButton(
                    "Set Location on Map",
                    new Point(20, 390),
                    new Size(150, 30),
                    (s, e) => EnableMapLocationSelection()
                );
                leftPanel.Controls.Add(setLocationButton);

                // Add address search control
                addressSearchControl = new AddressSearchControl(mapService, gMapControl)
                {
                    Location = new Point(20, 430),
                    Size = new Size(310, 50)
                };
                addressSearchControl.AddressFound += AddressSearchControl_AddressFound;
                leftPanel.Controls.Add(addressSearchControl);

                // Add instructions label
                locationInstructionsLabel = ControlExtensions.CreateLabel(
                    "Click on the map to set your starting location",
                    new Point(20, 490),
                    new Size(310, 20),
                    null,
                    ContentAlignment.MiddleCenter
                );
                locationInstructionsLabel.ForeColor = Color.Red;
                locationInstructionsLabel.Visible = false;
                leftPanel.Controls.Add(locationInstructionsLabel);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error setting up location controls: {ex.Message}",
                    "UI Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AddressSearchControl_AddressFound(object sender, AddressFoundEventArgs e)
        {
            try
            {
                // When an address is found by the AddressSearchControl, update the vehicle location
                UpdateVehicleLocation(e.Latitude, e.Longitude, e.FormattedAddress);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error processing address: {ex.Message}",
                    "Address Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task LoadDriverDataAsync()
        {
            if (refreshButton == null || routeDetailsTextBox == null) return;

            refreshButton.Enabled = false;
            routeDetailsTextBox.Clear();
            routeDetailsTextBox.AppendText("Loading route data...\n");

            try
            {
                // Load vehicle and route data
                vehicle = await dbService.GetVehicleByUserIdAsync(userId);

                if (vehicle == null)
                {
                    routeDetailsTextBox.Clear();
                    routeDetailsTextBox.AppendText("No vehicle is assigned to you.\n");
                    routeDetailsTextBox.AppendText("Please contact your administrator to set up your vehicle.\n");
                    return;
                }

                // Update availability checkbox
                availabilityCheckBox.Checked = vehicle.IsAvailableTomorrow;

                // Get today's date in the format used by the database
                string today = DateTime.Now.ToString("yyyy-MM-dd");

                // Get route data
                var routeData = await dbService.GetDriverRouteAsync(userId, today);
                vehicle = routeData.Vehicle ?? vehicle;
                assignedPassengers = routeData.Passengers ?? new List<Passenger>();
                pickupTime = routeData.PickupTime;

                // Clear map and display the route
                ShowRouteOnMap();

                // Update route details text
                UpdateRouteDetailsText(routeData.PickupTime);

                // Update the address search control with current address
                if (addressSearchControl != null && !string.IsNullOrEmpty(vehicle.StartAddress))
                {
                    addressSearchControl.Address = vehicle.StartAddress;
                }
            }
            catch (Exception ex)
            {
                routeDetailsTextBox.Clear();
                routeDetailsTextBox.AppendText($"Error loading data: {ex.Message}\n");
            }
            finally
            {
                refreshButton.Enabled = true;
            }
        }

        private void ShowRouteOnMap()
        {
            if (gMapControl == null || vehicle == null) return;

            try
            {
                gMapControl.Overlays.Clear();

                var vehiclesOverlay = new GMapOverlay("vehicles");
                var passengersOverlay = new GMapOverlay("passengers");
                var routesOverlay = new GMapOverlay("routes");
                var destinationOverlay = new GMapOverlay("destination");

                // Show vehicle marker
                var vehicleMarker = MapOverlays.CreateVehicleMarker(vehicle);
                vehiclesOverlay.Markers.Add(vehicleMarker);

                // Center map on vehicle location
                gMapControl.Position = new PointLatLng(vehicle.StartLatitude, vehicle.StartLongitude);
                gMapControl.Zoom = 13;

                // Show passenger markers and create route points
                if (assignedPassengers != null && assignedPassengers.Count > 0)
                {
                    foreach (var passenger in assignedPassengers)
                    {
                        if (passenger != null)
                        {
                            var passengerMarker = MapOverlays.CreatePassengerMarker(passenger);
                            passengersOverlay.Markers.Add(passengerMarker);
                        }
                    }
                }

                // Add overlays to map
                gMapControl.Overlays.Add(routesOverlay);
                gMapControl.Overlays.Add(vehiclesOverlay);
                gMapControl.Overlays.Add(passengersOverlay);
                gMapControl.Overlays.Add(destinationOverlay);

                // Get destination from database
                Task.Run(async () => {
                    try
                    {
                        var destination = await dbService.GetDestinationAsync();

                        this.Invoke(new Action(() => {
                            try
                            {
                                // Add destination marker
                                var destMarker = MapOverlays.CreateDestinationMarker(
                                    destination.Latitude, destination.Longitude);

                                // Create a new overlay since we're on a different thread
                                var newDestOverlay = new GMapOverlay("destination");
                                newDestOverlay.Markers.Add(destMarker);
                                gMapControl.Overlays.Add(newDestOverlay);

                                // Create route if we have passengers
                                if (assignedPassengers != null && assignedPassengers.Count > 0)
                                {
                                    var routePoints = new List<PointLatLng>();
                                    routePoints.Add(new PointLatLng(vehicle.StartLatitude, vehicle.StartLongitude));

                                    foreach (var passenger in assignedPassengers)
                                    {
                                        routePoints.Add(new PointLatLng(passenger.Latitude, passenger.Longitude));
                                    }

                                    routePoints.Add(new PointLatLng(destination.Latitude, destination.Longitude));

                                    // Create route
                                    var newRoute = MapOverlays.CreateRoute(routePoints, "DriverRoute", Color.Blue);
                                    var newRouteOverlay = new GMapOverlay("route");
                                    newRouteOverlay.Routes.Add(newRoute);
                                    gMapControl.Overlays.Add(newRouteOverlay);
                                }

                                gMapControl.Refresh();
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error adding destination marker: {ex.Message}");
                            }
                        }));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error getting destination: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error displaying route: {ex.Message}",
                    "Map Display Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void UpdateRouteDetailsText(DateTime? departureTime)
        {
            if (routeDetailsTextBox == null) return;

            routeDetailsTextBox.Clear();

            if (vehicle == null)
            {
                routeDetailsTextBox.AppendText("No vehicle assigned.\n");
                return;
            }

            routeDetailsTextBox.SelectionFont = new Font(routeDetailsTextBox.Font, FontStyle.Bold);
            routeDetailsTextBox.AppendText("Your Vehicle Details:\n");
            routeDetailsTextBox.SelectionFont = routeDetailsTextBox.Font;
            routeDetailsTextBox.AppendText($"Vehicle ID: {vehicle.Id}\n");
            routeDetailsTextBox.AppendText($"Capacity: {vehicle.Capacity}\n");

            if (!string.IsNullOrEmpty(vehicle.StartAddress))
            {
                routeDetailsTextBox.AppendText($"Starting Location: {vehicle.StartAddress}\n\n");
            }
            else
            {
                routeDetailsTextBox.AppendText($"Starting Location: ({vehicle.StartLatitude:F6}, {vehicle.StartLongitude:F6})\n\n");
            }

            if (assignedPassengers != null && assignedPassengers.Count > 0)
            {
                routeDetailsTextBox.SelectionFont = new Font(routeDetailsTextBox.Font, FontStyle.Bold);
                routeDetailsTextBox.AppendText("Assigned Passengers:\n");
                routeDetailsTextBox.SelectionFont = routeDetailsTextBox.Font;

                for (int i = 0; i < assignedPassengers.Count; i++)
                {
                    var passenger = assignedPassengers[i];
                    if (passenger == null) continue;

                    routeDetailsTextBox.AppendText($"{i + 1}. {passenger.Name}\n");

                    if (!string.IsNullOrEmpty(passenger.Address))
                        routeDetailsTextBox.AppendText($"   Pick-up: {passenger.Address}\n");
                    else
                        routeDetailsTextBox.AppendText($"   Pick-up: ({passenger.Latitude:F6}, {passenger.Longitude:F6})\n");
                }

                routeDetailsTextBox.AppendText("\n");

                if (departureTime.HasValue)
                {
                    routeDetailsTextBox.SelectionFont = new Font(routeDetailsTextBox.Font, FontStyle.Bold);
                    routeDetailsTextBox.AppendText($"Scheduled Departure: {departureTime.Value.ToString("HH:mm")}\n");
                }
            }
            else
            {
                routeDetailsTextBox.AppendText("No passengers assigned for today's route.\n");
            }
        }

        private async Task UpdateAvailabilityAsync()
        {
            if (vehicle == null || availabilityCheckBox == null)
                return;

            try
            {
                bool success = await dbService.UpdateVehicleAvailabilityAsync(vehicle.Id, availabilityCheckBox.Checked);

                if (success)
                {
                    vehicle.IsAvailableTomorrow = availabilityCheckBox.Checked;

                    if (vehicle.IsAvailableTomorrow)
                    {
                        MessageBox.Show("You are now marked as available to drive tomorrow.",
                            "Availability Updated", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        MessageBox.Show("You are now marked as unavailable to drive tomorrow.",
                            "Availability Updated", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                else
                {
                    MessageBox.Show("Failed to update availability. Please try again.",
                        "Update Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                    // Revert checkbox to match database state
                    availabilityCheckBox.CheckedChanged -= async (s, e) => await UpdateAvailabilityAsync();
                    availabilityCheckBox.Checked = vehicle.IsAvailableTomorrow;
                    availabilityCheckBox.CheckedChanged += async (s, e) => await UpdateAvailabilityAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating availability: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

                // Revert checkbox to match database state
                availabilityCheckBox.CheckedChanged -= async (s, e) => await UpdateAvailabilityAsync();
                availabilityCheckBox.Checked = vehicle.IsAvailableTomorrow;
                availabilityCheckBox.CheckedChanged += async (s, e) => await UpdateAvailabilityAsync();
            }
        }

        /// <summary>
        /// Enables map location selection mode
        /// </summary>
        private void EnableMapLocationSelection()
        {
            try
            {
                isSettingLocation = true;
                locationInstructionsLabel.Visible = true;

                // Change cursor to indicate map is clickable
                gMapControl.Cursor = Cursors.Hand;

                // Add event handler for map clicks
                gMapControl.MouseClick += GMapControl_MouseClick;

                MessageBox.Show("Click on the map to set your starting location",
                    "Set Location", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error enabling location selection: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                isSettingLocation = false;
            }
        }

        /// <summary>
        /// Handles map click events to set location
        /// </summary>
        private void GMapControl_MouseClick(object sender, MouseEventArgs e)
        {
            if (!isSettingLocation) return;

            try
            {
                // Convert clicked point to geo coordinates
                PointLatLng point = gMapControl.FromLocalToLatLng(e.X, e.Y);

                // Get address for the clicked location
                Task.Run(async () =>
                {
                    try
                    {
                        string address = await mapService.ReverseGeocodeAsync(point.Lat, point.Lng);

                        // Update vehicle location
                        this.Invoke(new Action(() =>
                        {
                            UpdateVehicleLocation(point.Lat, point.Lng, address);
                            isSettingLocation = false;
                            locationInstructionsLabel.Visible = false;
                            gMapControl.Cursor = Cursors.Default;

                            // Remove the click handler
                            gMapControl.MouseClick -= GMapControl_MouseClick;
                        }));
                    }
                    catch (Exception ex)
                    {
                        this.Invoke(new Action(() =>
                        {
                            MessageBox.Show($"Error getting address: {ex.Message}",
                                "Geocoding Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            isSettingLocation = false;
                            locationInstructionsLabel.Visible = false;
                            gMapControl.Cursor = Cursors.Default;

                            // Remove the click handler
                            gMapControl.MouseClick -= GMapControl_MouseClick;
                        }));
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error processing map click: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                isSettingLocation = false;
                locationInstructionsLabel.Visible = false;
                gMapControl.Cursor = Cursors.Default;

                // Remove the click handler
                gMapControl.MouseClick -= GMapControl_MouseClick;
            }
        }

        /// <summary>
        /// Updates the vehicle location in the database and UI
        /// </summary>
        private async void UpdateVehicleLocation(double latitude, double longitude, string address = null)
        {
            try
            {
                // Show waiting cursor
                Cursor = Cursors.WaitCursor;

                // Attempt to get address if not provided
                if (string.IsNullOrEmpty(address))
                {
                    address = await mapService.ReverseGeocodeAsync(latitude, longitude);
                }

                // Update vehicle in database
                if (vehicle == null)
                {
                    MessageBox.Show("No vehicle is assigned to you. Please contact your administrator.",
                        "No Vehicle", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                bool success = await dbService.UpdateVehicleAsync(
                    vehicle.Id,
                    vehicle.Capacity,
                    latitude,
                    longitude,
                    address
                );

                if (success)
                {
                    // Update local vehicle data
                    vehicle.StartLatitude = latitude;
                    vehicle.StartLongitude = longitude;
                    vehicle.StartAddress = address;

                    // Update address in search control
                    if (addressSearchControl != null)
                        addressSearchControl.Address = address;

                    // Show confirmation and update marker on map
                    MessageBox.Show($"Your starting location has been set to:\n{address}",
                        "Location Updated", MessageBoxButtons.OK, MessageBoxIcon.Information);

                    // Refresh map display
                    ShowRouteOnMap();

                    // Refresh vehicle details
                    await LoadDriverDataAsync();
                }
                else
                {
                    MessageBox.Show("Failed to update location. Please try again.",
                        "Update Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating location: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                // Reset cursor
                Cursor = Cursors.Default;
            }
        }
    }
}