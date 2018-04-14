﻿using DomainModels.Types;
using System;
using System.Collections;

namespace DomainModels.Vehicle
{
    [Serializable]
    public class VehicleModel : DomainModel<Vehicle>
    {

    }
    [Serializable]
    public class Vehicle
    {
        public string ChassisNumber { get; set; }
        public string Model { get; set; }
        public string Color { get; set; }
        public string ProductionYear { get; set; }
        public string ProductoinFactory { get; set; }
        public IDictionary Features { get; set; }

    }
}
