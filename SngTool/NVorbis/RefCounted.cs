// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Modified copy of System/Runtime/InteropServices/SafeHandle.cs

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;

namespace VoxelPizza.Memory;

/// <summary>
/// Represents a tracker used for safely managing a resource or allocation.
/// </summary>
[DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
internal abstract class RefCounted
{
    /// <summary>Combined ref count and closed/disposed flags (so we can atomically modify them).</summary>
    private volatile nint _state;

    public bool IsClosed => (_state & StateBits.Closed) == StateBits.Closed;

    public bool IsDisposed => (_state & StateBits.Disposed) == StateBits.Disposed;

    public bool HasTarget => !IsClosed;

    public nint Count => GetCount(_state);

    public RefCounted()
    {
        ResetState();
    }

    protected void ResetState()
    {
        _state = StateBits.RefCountOne; // Ref count 1 and not closed or disposed.
    }

    internal nint GetState()
    {
        return _state;
    }

    /// <inheritdoc/>
    public bool TryIncrement()
    {
        // To prevent handle recycling security attacks we must enforce the
        // following invariant: we cannot successfully Increment a handle on which
        // we've committed to the process of releasing.

        // We ensure this by never Increment'ing a handle that is marked closed and
        // never marking a handle as closed while the ref count is non-zero. For
        // this to be thread safe we must perform inspection/updates of the two
        // values as a single atomic operation. We achieve this by storing them both
        // in a single aligned int and modifying the entire state via interlocked
        // compare exchange operations.

        // Additionally we have to deal with the problem of the Dispose operation.
        // We must assume that this operation is directly exposed to untrusted
        // callers and that malicious callers will try and use what is basically a
        // Release call to decrement the ref count to zero and free the handle while
        // it's still in use (the other way a handle recycling attack can be
        // mounted). We combat this by allowing only one Dispose to operate against
        // a given safe handle (which balances the creation operation given that
        // Dispose suppresses finalization). We record the fact that a Dispose has
        // been requested in the same state field as the ref count and closed state.

        // Might have to perform the following steps multiple times due to
        // interference from other Increment's and Release's.
        nint oldState, newState;
        do
        {
            // First step is to read the current handle state. We use this as a
            // basis to decide whether an Increment is legal and, if so, to propose an
            // update predicated on the initial state (a conditional write).
            // Check for closed state.
            oldState = _state;
            if ((oldState & StateBits.Closed) != 0)
            {
                return false;
            }

            // Not closed, let's propose an update (to the ref count, just add
            // StateBits.RefCountOne to the state to effectively add 1 to the ref count).
            // Continue doing this until the update succeeds (because nobody
            // modifies the state field between the read and write operations) or
            // the state moves to closed.
            newState = oldState + StateBits.RefCountOne;
        }
        while (Interlocked.CompareExchange(ref _state, newState, oldState) != oldState);

        // If we got here we managed to update the ref count while the state
        // remained non closed. So we're done.
        return true;
    }

    /// <inheritdoc/>
    public void IncrementRef()
    {
        if (!TryIncrement())
        {
            ThrowObjectDisposed();
        }
    }

    /// <inheritdoc/>
    public void DecrementRef()
    {
        DecrementCore(disposeOrFinalizeOperation: false);
    }

    public void DecrementDispose()
    {
        DecrementCore(disposeOrFinalizeOperation: true);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DecrementCore(bool disposeOrFinalizeOperation)
    {
        // See Increment above for the design of the synchronization here. Basically we
        // will try to decrement the current ref count and, if that would take us to
        // zero refs, set the closed state on the handle as well.
        bool performRelease;

        // Might have to perform the following steps multiple times due to
        // interference from other Increment's and Decrement's.
        nint oldState, newState;
        do
        {
            // First step is to read the current handle state. We use this cached
            // value to predicate any modification we might decide to make to the
            // state).
            oldState = _state;

            // If this is a Dispose operation we have additional requirements (to
            // ensure that Dispose happens at most once as the comments in Increment
            // detail). We must check that the dispose bit is not set in the old
            // state and, in the case of successful state update, leave the disposed
            // bit set. Silently do nothing if Dispose has already been called.
            if (disposeOrFinalizeOperation && ((oldState & StateBits.Disposed) != 0))
            {
                return;
            }

            // We should never see a ref count of zero (that would imply we have
            // unbalanced Increment and Decrements). (We might see a closed state before
            // hitting zero though -- that can happen if SetHandleAsInvalid is
            // used).
            if ((oldState & StateBits.RefCount) == 0)
            {
                ThrowObjectDisposed();
            }

            // If we're proposing a decrement to zero and the handle is not closed
            // and we own the handle then we need to release the handle upon a
            // successful state update. [[If so we need to check whether the handle is
            // currently invalid by asking the SafeHandle subclass. We must do this before
            // transitioning the handle to closed, however, since setting the closed
            // state will cause IsInvalid to always return true.]]
            performRelease = (oldState & (StateBits.RefCount | StateBits.Closed)) == StateBits.RefCountOne;

            // Attempt the update to the new state, fail and retry if the initial
            // state has been modified in the meantime. Decrement the ref count by
            // subtracting StateBits.RefCountOne from the state then OR in the bits for
            // Dispose (if that's the reason for the Release) and closed (if the
            // initial ref count was 1).
            newState = oldState - StateBits.RefCountOne;
            if ((oldState & StateBits.RefCount) == StateBits.RefCountOne)
            {
                newState |= StateBits.Closed;
            }
            if (disposeOrFinalizeOperation)
            {
                newState |= StateBits.Disposed;
            }
        }
        while (Interlocked.CompareExchange(ref _state, newState, oldState) != oldState);

        // If we get here we successfully decremented the ref count. Additionally we
        // may have decremented it to zero and set the handle state as closed. In
        // this case (providing we own the handle) we will call the ReleaseHandle
        // method on the SafeHandle subclass.
        if (performRelease)
        {
            Release();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    protected virtual void Release()
    {
#pragma warning disable CA1816 // Dispose methods should call SuppressFinalize
        GC.SuppressFinalize(this);
#pragma warning restore CA1816 // Dispose methods should call SuppressFinalize
    }

    private string GetDebuggerDisplay()
    {
        return GetDebuggerDisplay(GetType().Name, _state);
    }

    internal static string GetDebuggerDisplay(string type, nint state)
    {
        nint count = GetCount(state);

        string dFlag = "";
        if ((state & StateBits.Disposed) == StateBits.Disposed)
        {
            dFlag = "D";
        }

        string cFlag = "";
        if ((state & StateBits.Closed) == StateBits.Closed)
        {
            cFlag = "C";
        }

        string delim = dFlag.Length > 0 || cFlag.Length > 0 ? " " : "";
        return $"{type}@{count}{delim}{dFlag}{cFlag}";
    }

    [DoesNotReturn]
    protected static void ThrowObjectDisposed()
    {
        throw new ObjectDisposedException("An attempt was made to reference a disposed resource.");
    }

    private static nint GetCount(nint state)
    {
        // Unsigned div by pow-of-2 is optimized into a shift
        return (nint)((nuint)(state & StateBits.RefCount) / (nuint)StateBits.RefCountOne);
    }

    /// <summary>Bitmasks for the <see cref="_state"/> field.</summary>
    /// <remarks>
    /// The state field ends up looking like this:
    ///
    ///  63/31                                                     2  1   0
    /// +-----------------------------------------------------------+---+---+
    /// |                           Ref count                       | D | C |
    /// +-----------------------------------------------------------+---+---+
    ///
    /// Where D = 1 means a Dispose has been performed and C = 1 means the
    /// underlying handle has been (or will be shortly) released.
    /// </remarks>
    private static class StateBits
    {
        public const int Closed = 0b01;
        public const int Disposed = 0b10;
        public const nint RefCount = unchecked(~0b11); // 2 bits reserved for closed/disposed; ref count gets the rest
        public const nint RefCountOne = 1 << 2;
    }
}
