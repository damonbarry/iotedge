﻿// Copyright (c) Microsoft. All rights reserved.
namespace NetworkController
{
    using System;
    using System.Runtime.Serialization;

    class CommandExecutionException : Exception
    {
        public CommandExecutionException()
        {
        }

        public CommandExecutionException(string message)
            : base(message)
        {
        }

        public CommandExecutionException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
