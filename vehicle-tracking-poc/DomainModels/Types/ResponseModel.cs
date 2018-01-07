﻿using System;
using System.Collections.Generic;
using System.Text;

namespace DomainModels.Types
{
    public sealed class ResponseModel<T> : ResponseHeader
    {
        public T Body { get; set; }
    }
}
