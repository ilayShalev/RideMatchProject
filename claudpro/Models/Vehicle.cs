﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace claudpro.Models
{
    public class Vehicle
    {
        public int Id { get; set; }
        public int Capacity { get; set; }
        public double StartLatitude { get; set; }
        public double StartLongitude { get; set; }
        public string StartAddress { get; set; }
        public List<Passenger> AssignedPassengers { get; set; } = new List<Passenger>();
        public double TotalDistance { get; set; }
        public double TotalTime { get; set; }

        // New properties for database integration
        public int UserId { get; set; }
        public string DriverName { get; set; }
        public bool IsAvailableTomorrow { get; set; } = true;

        public override string ToString()
        {
            string displayName = !string.IsNullOrEmpty(DriverName) ? DriverName : $"Vehicle {Id}";

            if (!string.IsNullOrEmpty(StartAddress))
                return $"{displayName} (Capacity: {Capacity}, {StartAddress})";

            return $"{displayName} (Capacity: {Capacity})";
        }
    }
}