// Copyright (c) Microsoft. All rights reserved.
namespace Microsoft.Azure.Devices.Edge.Hub.Amqp
{
    using Microsoft.Azure.Amqp;
    using Microsoft.Azure.Amqp.Encoding;
    using Microsoft.Azure.Devices.Common.Exceptions;

    public static class AmqpErrorMapper
    {
        // Error codes
        static readonly AmqpSymbol ArgumentError = AmqpConstants.Vendor + ":argument-error";
        static readonly AmqpSymbol DeviceContainerThrottled = AmqpConstants.Vendor + ":device-container-throttled";
        static readonly AmqpSymbol PreconditionFailed = AmqpConstants.Vendor + ":precondition-failed";

        // Maps the ErrorCode of an IotHubException into an appropriate AMQP error code
        public static AmqpSymbol GetErrorCondition(ErrorCode errorCode)
        {
            switch (errorCode)
            {
                case ErrorCode.InvalidOperation:
                    return AmqpErrorCode.NotAllowed;

                case ErrorCode.ArgumentInvalid:
                case ErrorCode.ArgumentNull:
                    return ArgumentError;

                case ErrorCode.IotHubUnauthorizedAccess:
                    return AmqpErrorCode.UnauthorizedAccess;

                case ErrorCode.DeviceNotFound:
                    return AmqpErrorCode.NotFound;

                case ErrorCode.IotHubQuotaExceeded:
                case ErrorCode.DeviceMaximumQueueDepthExceeded:
                    return AmqpErrorCode.ResourceLimitExceeded;

                case ErrorCode.PreconditionFailed:
                    return PreconditionFailed;

                case ErrorCode.MessageTooLarge:
                    return AmqpErrorCode.MessageSizeExceeded;

                case ErrorCode.ThrottlingException:
                    return DeviceContainerThrottled;

                default:
                    return AmqpErrorCode.InternalError;
            }
        }
    }
}
