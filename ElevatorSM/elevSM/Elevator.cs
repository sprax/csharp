// Elevator.cs defines class Elevator which uses a state machine
// AUTH:    Sprax
// DATE:    2012 Oct

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Timers;

// using ElevatorAndSM.ElevSM;

namespace ElevatorAndSM
{
        public enum RequestType { STOP_AT, CALL_UP, CALL_DN, HALT_AT, UN_HALT };   // HALT and UNHALT must be the last 2
        public struct Request
        {
            public RequestType mType;
            public int         mFloor;
            public Request(RequestType type, int floor) { mType = type; mFloor = floor; }
        }

    // This model of an elevator wraps up the the state machine + domain
    // logic + elevator mechanics all inside the Elevator class.  
    // Updates are effected from a timer loop, and there is no locking 
    // or synchronization.  The only guard against race conditions is in
    // the relative timing.  In particular, the
    // elevator's departure from one floor and arrival at another floor
    // are not modeled as separate events.  Rather, the moveUp and moveDown
    // methods always increment or decrement the current floor, which implies
    // that they should execute atomically.  At the very least, they must be
    // finished executing and making any state changes before the 
    // timing loop can possibly call them again.
    //
    // For example, if a delay were to be introduced inside
    // of moveUp, the elevator may get into an inconsistent state, such as 
    // as rising above the top floor.  In particular, if moveUp were delayed
    // before it could increment the elevator's current floor, then the update
    // loop could possibly call it again during that delay, resulting in the
    // current floor being incremented multiple times when it should have only
    // been incremented once.  This could make the elevator seem to skip a 
    // floor or go through the roof.  Even re-checking the context before 
    // incrementing may not remedy this race condition, because a second
    // update/increment operation might be called before the first has even
    // incremented the current floor.  If, on the other hand, there were a 
    // delay between the floor change and the state change, adding some 
    // double-checking or other extra logic may let the later update escape 
    // from the race condition, but it will not prevent it from entering the
    // race condition in the first place.
    //
    // The way to avoid such race conditions using timing along is to make 
    // sure that the time it takes for the model to service a request is 
    // much less than the timing period.  This is something of an inversion
    // of the real world, where you would want the elevator controller to
    // update itself far more frequently than the time it takes the elevator
    // to move from one floor to the next.
    //
    // Conversely, to bring about the race condition using this model, just
    // make mPeriodMs < mWaitTimeMs. 
    //
    // A more realistic model might separate the state machine and elevator
    // mechanics into separate objects, and decouple their actions into a
    // more asynchronous set of interactions.  
    //
    // As it is now, the Elevator starts the state machine, whose States
    // call back into elevator to make things happen.  So Elevator is
    // both the container for and the client of the state machine.
    //
    // TODO: Mode the time it takes for elevator to move between floors:
    // instead of 1 floor per update, change the floor based on elapsed time
    // since start of movement and last floor passed.
    //
    // Allowed Transitions Table:           WAIT    RISE    SINK    HALT
    //                              WAIT            X       X       X
    //     Note: No State           RISE    X                       X
    //     can transition           SINK    X                       X
    //     to itself.               HALT    X       X       X       
    // 
    // TODO: RISE = < CloseDoors, Ascent, OpenDoors >, etc.
    
    // Elevator containing an elevator state machine
    public class Elevator
    {
        private ElevSM mElevSM;

        public readonly int mMinFloor;
        public readonly int mMaxFloor;
        public readonly int mNumFloors;
        private bool[] mElevStops;
        private bool[] mWallUpReqs;
        private bool[] mWallDownReqs;
        //private      int      mNumUpReqs;
        //private      int      mNumDownReqs;

        private  int mCurrentFloor;
        internal int mVerbose = 3;

        // Simulation time scale can be a fraction of the world time scale.
        // For instance, if we expect a real elevator to take 4.4 seconds to 
        // go up exactly one floor, we can model it at 1/100 scale as 44 Ms 
        // (measured from the time the doors start to close on one floor 
        // and finish opening one floor up).  All characteristic times
        // scale as mUpdatePeriodMs/mDefaultUpdatePeriodMs.
        // 
        internal readonly int              mStartMs  =    33;
        internal readonly int       mUpdatePeriodMs  =   100; // Actual update timing interval.  All times below scale with this!
        private const    int mDefaultUpdatePeriodMs  =   100; // Default update timing interval
        internal readonly int    mMinTimeDoorsOpenMs =  4567; // min time to keep doors open, waiting for new requests 
        internal readonly int mMinTimeBeforeUnHaltMs =  2000; // min time to stay halted (self-tests, etc.) 
        internal readonly int mTimeToRiseOneFloorMs  =  4321; // About 4.4 seconds "real time", i.e. before scaling.
        internal readonly int mTimeToRiseAnotherMs   =  2222;
        internal readonly int mTimeToSinkOneFloorMs  =  3777;
        internal readonly int mTimeToSinkAnotherMs   =  1777;
        internal readonly int    mMaxInterReqTimeMs  =  1667; // Test: max time to allow between simulated requests
        internal          int       mEtaNextFloorMs;

        internal readonly String mName;

        // constructor
        public Elevator(String name, int minFloor, int maxFloor, int periodMs)
        {
            if (name != null)
                mName = name;
            else
                mName = this.GetType().ToString();

            mMinFloor = minFloor;
            mMaxFloor = maxFloor;
            mNumFloors = maxFloor - minFloor + 1;
            mElevStops = new bool[mNumFloors];
            mWallUpReqs = new bool[mNumFloors];
            mWallDownReqs = new bool[mNumFloors];

            Debug.Assert(periodMs > 0);
            mUpdatePeriodMs = periodMs;
            if (mUpdatePeriodMs < mDefaultUpdatePeriodMs)
            {
                double scale = (double) mUpdatePeriodMs / mDefaultUpdatePeriodMs;
                mMinTimeDoorsOpenMs = (int) (scale * mMinTimeDoorsOpenMs);
                mMinTimeBeforeUnHaltMs = (int) (scale * mMinTimeBeforeUnHaltMs);
                mTimeToRiseOneFloorMs = (int) (scale * mTimeToRiseOneFloorMs);
                mTimeToRiseAnotherMs = (int) (scale * mTimeToRiseAnotherMs);
                mTimeToSinkOneFloorMs = (int) (scale * mTimeToSinkOneFloorMs);
                mTimeToSinkAnotherMs = (int) (scale * mTimeToSinkAnotherMs);
                mMaxInterReqTimeMs = (int) (scale * mMaxInterReqTimeMs);
            }

            mElevSM = new ElevSM(this, mVerbose);
        }

        internal ElevSM getSM() { return mElevSM; }

        //private void setState(IState state)
        //{
        //    mFSM.setState(state);
        //}

        public int getFloorNow() { return mCurrentFloor; }


        internal bool addStopRequest(int floor)
        {
            if (mElevStops[floor] == false)
                return mElevStops[floor] = true;
            return false;
        }

        internal bool addCallUpRequest(int floor)
        {
            if (mWallUpReqs[floor] == false)
                return mWallUpReqs[floor] = true;
            return false;
        }

        internal bool addCallDownRequest(int floor)
        {
            if (mWallDownReqs[floor] == false)
                return mWallDownReqs[floor] = true;
            return false;
        }

        internal void reportRequestHandled(RequestType reqType, int floor, bool isNew, IState handlerState)
        {
            String compare = floor < mCurrentFloor ? "<" : (floor > mCurrentFloor ? ">" : "=");
            String novelty = isNew ? "New" : (floor == mCurrentFloor ? "No-op" : "dupe");
            Sx.format("     Hndl by {0}:  {1} {2}  {3}  {4}:  {5}\n"
                    , handlerState.ToString(), reqType, floor, compare, mCurrentFloor, novelty);
            Debug.Assert(mElevSM.getState() == handlerState);
        }

        internal void reportHaltRequestHandled(RequestType reqType, bool isNew, IState handlerState)
        {
            Sx.format("     Hndl by {0}:  {1} {2}      :  {3}\n"
                    , handlerState, reqType, mCurrentFloor
                    , (isNew ? "New" : "No-op"));
        }


        /************************************************************************/
        /* Elevator mechanics methods, called by States update methods          */
        /************************************************************************/

        internal void waitAt()
        {
            if (mVerbose > 0)
                Sx.format("{0} Wait at {1,2}\n", mElevSM.getState(), mCurrentFloor);
        }

        internal void moveUp()
        {
            if (mCurrentFloor >= mMaxFloor) // TODO: replace this check with an assertion
            {
                Sx.format(">>>>>>>> Invalid RISE state: current floor {0} >= {1} (max)\n", mCurrentFloor, mMaxFloor);
                mElevSM.setWaitingState();
            }
            int nextStop = nextUpStop();        // re-compute next stop
            if (++mCurrentFloor < nextStop)     // One floor up is still lower than next stop, so keep going.
            {
                mEtaNextFloorMs += mTimeToRiseAnotherMs;   // Keep rising, so it's the shorter time to next floor.
                if (mVerbose > 0)
                    Sx.format("{0} Pass up {1,2} en route to {2}\n", mElevSM.getState(), mCurrentFloor, nextStop);
                return;
            }

            Debug.Assert(mCurrentFloor == nextStop);
            if (mVerbose > 0)
            {
                String reason;
                if (mElevStops[mCurrentFloor])
                {
                    mElevStops[mCurrentFloor] = false;
                    reason = "for exiting";
                    if (mWallUpReqs[mCurrentFloor])
                    {
                        mWallUpReqs[mCurrentFloor] = false;
                        reason = "for exiting and up-boarding";
                    }
                }
                else if (mWallUpReqs[mCurrentFloor])
                {
                    mWallUpReqs[mCurrentFloor] = false;
                    reason = "for up-boarding";
                }
                else
                {
                    Debug.Assert(mWallDownReqs[mCurrentFloor]);
                    mWallDownReqs[mCurrentFloor] = false;
                    reason = "for down-boarding";
                }
                Sx.format("{0} Stop at {1,2} {2}\n", mElevSM.getState(), mCurrentFloor, reason);
            }
            else // not verbose
            {
                if (mElevStops[mCurrentFloor])
                {
                    mElevStops[mCurrentFloor] = false;
                    if (mWallUpReqs[mCurrentFloor])
                    {
                        mWallUpReqs[mCurrentFloor] = false;
                    }
                }
                else if (mWallUpReqs[mCurrentFloor])
                {
                    mWallUpReqs[mCurrentFloor] = false;
                }
                else
                {
                    Debug.Assert(mWallDownReqs[mCurrentFloor]);
                    mWallDownReqs[mCurrentFloor] = false;
                }
            }

            // Now, if the elevator is at the highest requested stop, 
            // wait for more input before starting down.  That is, go
            // into the WAIT state for at least one update cycle.
            // During that cycle, a down-boarder may press a higher stop
            // button, or someone on a higher floor may press the UP or 
            // DOWN call button.

            // So, recompute the next up stop, and if it is not higher, go to WAIT.
            nextStop = nextUpStop();
            if (nextStop <= mCurrentFloor)
                mElevSM.setWaitingState();
            else
                mEtaNextFloorMs += mTimeToRiseOneFloorMs;    // Stopped, so longer time to next floor
        }

        internal void moveDown() // TODO: handle non-verbose 
        {
            if (mCurrentFloor <= mMinFloor) // TODO: replace this check with an assertion
            {
                Sx.format(">>>>>>>> Invalid SINK state: current floor {0} <= {1} (max)\n", mCurrentFloor, mMinFloor);
                mElevSM.setWaitingState();
            }
            int nextStop = nextDownStop();      // re-compute next stop
            if (--mCurrentFloor > nextStop)     // One floor down is still higher than next stop, so keep going.
            {
                mEtaNextFloorMs += mTimeToSinkAnotherMs;   // Keep sinking, so it's the shorter time to the next floor.
                if (mVerbose > 0)
                    Sx.format("{0} Pass by {1,2} en route to {2}\n", mElevSM.getState(), mCurrentFloor, nextStop);
                return;
            }

            Debug.Assert(mCurrentFloor == nextStop);
            if (mVerbose > 0)
            {
                String reason;
                if (mElevStops[mCurrentFloor])
                {
                    mElevStops[mCurrentFloor] = false;
                    reason = "for exiting";
                    if (mWallDownReqs[mCurrentFloor])
                    {
                        mWallDownReqs[mCurrentFloor] = false;
                        reason = "for exiting and down-boarding";
                    }
                }
                else if (mWallDownReqs[mCurrentFloor])
                {
                    mWallDownReqs[mCurrentFloor] = false;
                    reason = "for down-boarding";
                }
                else
                {
                    Debug.Assert(mWallUpReqs[mCurrentFloor]);
                    mWallUpReqs[mCurrentFloor] = false;
                    reason = "for up-boarding";
                }
                Sx.format("{0} Stop at {1,2} {2}\n", mElevSM.getState(), mCurrentFloor, reason);
            }

            // Now, if the elevator is at the lowest requested stop, 
            // wait for more input before starting up again.  That is, go
            // into the WAIT state for at least one update cycle.
            // During that WAIT time, an up-boarder may press a lower stop
            // button, or someone on a lower floor may press the UP or 
            // DOWN call button.

            // So, recompute the next down stop, and if it is not lower, go to WAIT.
            nextStop = nextDownStop();
            if (nextStop >= mCurrentFloor)
                mElevSM.setWaitingState();
            else
                mEtaNextFloorMs += mTimeToSinkOneFloorMs;    // Stopped, so longer time to next floor
        }

        internal bool haltNow(IState handlerState)
        {
            if (mVerbose > 1)
            {
                reportHaltRequestHandled(RequestType.HALT_AT, true, handlerState);
            }
            return true;
        }

        internal bool unHalt(IState handlerState)
        {
            if (mVerbose > 1)
            {
                reportHaltRequestHandled(RequestType.UN_HALT, true, handlerState);
            }
            if (mVerbose > 0)
                Sx.format("{0} unHalt  {1}\n", mElevSM.getState(), mCurrentFloor);
            return true;
        }

        /************************************************************************/
        /* Elevator logic: decide the direction and floor of the next stop      */
        /************************************************************************/

        public int nextUpStop()
        {
            // Find the next stop or call-up request above the current floor.
            int floor = mCurrentFloor;
            while (++floor <= mMaxFloor)
            {
                if (mElevStops[floor] || mWallUpReqs[floor])
                    return floor;
            }
            // If there were no more stop or call-up requests above,
            // there must be at least one call-down request up there.
            // Find the *highest* one.  The elevator will stop at any 
            // lower call-down floors on the way down from the topmost one.
            while (--floor > mCurrentFloor)
            {
                if (mWallDownReqs[floor])
                    return floor;
            }
            // Still nothing?  Maybe it's an error or cancellation.
            return mCurrentFloor;   // Should result in a no-op/reset.
        }

        public int nextDownStop()
        {
            // Find the next stop or call-down request below the current floor.
            int floor = mCurrentFloor;
            while (--floor >= 0)
            {
                if (mElevStops[floor] || mWallDownReqs[floor])
                    return floor;
            }
            // If there were no more stop or call-down requests below,
            // then there must be at least one call-up request down there.
            // Find the lowest one.
            while (++floor < mNumFloors)
            {
                if (mWallUpReqs[floor])
                    return floor;
            }
            // Still nothing?  Maybe it's an error or cancellation.
            return mCurrentFloor;   // Should result in a no-op/reset.
        }

        /** return the total number of requested stops above current floor */
        public int numReqsUp()
        {
            int count = 0;
            for (int j = mCurrentFloor; ++j <= mMaxFloor; )
            {
                if (mElevStops[j])
                    count++;
                if (mWallUpReqs[j])
                    count += 1;     // Count it as one, even tho it will be followed by another
                if (mWallDownReqs[j])
                    count += 1;
            }
            return count;
        }

        /** return the total number of requested stops below current floor */
        public int numReqsDown()
        {
            int count = 0;
            for (int j = mCurrentFloor; --j >= 0; )
            {
                if (mElevStops[j])
                    count++;
                if (mWallUpReqs[j])
                    count += 1;
                if (mWallDownReqs[j])
                    count += 1;     // Count it as one, even tho it will be followed by another
            }
            return count;
        }

    }   // END: class Elevator
}
