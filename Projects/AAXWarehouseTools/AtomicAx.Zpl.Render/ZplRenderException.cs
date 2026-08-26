using System;

namespace AtomicAx.Zpl.Render
{
    /// <summary>
    /// The base exception for failures in the ZPL rendering library. Carries the original renderer
    /// message and, when available, the inner cause.
    /// </summary>
    public class ZplRenderException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ZplRenderException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the failure.</param>
        public ZplRenderException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ZplRenderException"/> class with an inner cause.
        /// </summary>
        /// <param name="message">The message that describes the failure.</param>
        /// <param name="inner">The exception that caused the failure.</param>
        public ZplRenderException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }

    /// <summary>
    /// Thrown when the label dimensions cannot be determined because the print density was not
    /// supplied, or the width or height was neither supplied nor declared in the ZPL by ^PW and ^LL.
    /// The caller is expected to fall back to its own default dimensions.
    /// </summary>
    public sealed class ZplDimensionsMissingException : ZplRenderException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ZplDimensionsMissingException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the missing dimension.</param>
        public ZplDimensionsMissingException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ZplDimensionsMissingException"/> class with an inner cause.
        /// </summary>
        /// <param name="message">The message that describes the missing dimension.</param>
        /// <param name="inner">The exception that caused the failure.</param>
        public ZplDimensionsMissingException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
