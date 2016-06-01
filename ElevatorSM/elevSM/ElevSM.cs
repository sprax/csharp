using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Timers;

namespace ElevatorAndSM
{
    interface IState { }


    class ElevSM
    {
        private State mState;
        private State mSaved;
        private State mHaltedState;
        private State mWaitingState;
        private State mRisingState;
        private State mSinkingState;

        private Elevator mElevator;
        private int mVerbose;
        private bool mUseEarlyDecision = true;

        private int mTimeLastStateChangeMs =   -1; 
        UpdateTimer mUpdateTimer;


        public ElevSM(Elevator elevator, int verbose)
        {
            mElevator = elevator;
            mVerbose = verbose;

            mWaitingState = new WaitingState();
            mRisingState = new RisingState();
            mSinkingState = new SinkingState();
            mHaltedState = new HaltedState();
            mState = mHaltedState;
        }

        internal IState getState()  // Unfortunately C# does not support return as readonly or const
        {
            return mState;
        }

        private void setState(State state)
        {
            if (mVerbose > 2)
                Sx.format("     {0} => {1}\n", mState, state);

            Debug.Assert(mState != state);                      // No self-loops
            mState.finish(this);
            mState = state;
            mTimeLastStateChangeMs = timeMs();
            mState.begin(this);
        }

        internal void setWaitingState() { setState(mWaitingState); }


        int timeMs()
        {
            return System.Environment.TickCount;
        }

        public void update(Object stateInfo)
        {
            // Elevator elevator = (Elevator) stateInfo;
            mState.update(this);
        }

        int getTimeInState()
        {
            return System.Environment.TickCount - mTimeLastStateChangeMs;
        }



        /************************************************************************/
        /* States for the state machine                                         */
        /************************************************************************/

        /** Base class for State Design Pattern states.
         *  Provides default event handlers for all user requests, but not update
         */
        abstract class State : IState
        {
            // methods: event handlers
            public virtual bool handleStopAt(ElevSM sm, int floor)
            {
                return sm.handleStopRequest(floor, this);
            }

            public virtual bool handleCallUpAt(ElevSM sm, int floor)
            {
                return sm.handleCallUpRequest(floor, this);
            }

            public virtual bool handleCallDownAt(ElevSM sm, int floor)
            {
                return sm.handleCallDownRequest(floor, this);
            }

            /** "Emergency" Stop requested */
            public virtual bool handleHalt(ElevSM sm)
            {
                return sm.haltTheElevator(this);
            }

            /** "Emergency" Stop canceled */
            public virtual bool handleUnHalt(ElevSM sm)
            {
                return sm.unHaltTheElevator(this);
            }
            public virtual void begin(ElevSM sm) { }
            public abstract void update(ElevSM sm);
            public virtual void finish(ElevSM sm) { }
        }

        class WaitingState : State
        {
            override public bool handleStopAt(ElevSM sm, int floor)
            {
                // elevator is just waiting idle, so immediately start moving to the requested floor 
                if (sm.handleStopRequest(floor, this))
                {
                    sm.setRiseOrSinkState(floor);
                    return true;
                }
                return false;
            }

            override public bool handleCallUpAt(ElevSM sm, int floor)
            {
                // elevator is just waiting idle, so immediately start moving to the requested floor 
                if (sm.handleCallUpRequest(floor, this))
                {
                    sm.setRiseOrSinkState(floor);
                    return true;
                }
                return false;
            }

            override public bool handleCallDownAt(ElevSM sm, int floor)
            {
                // elevator is just waiting idle, so immediately start moving to the requested floor 
                if (sm.handleCallDownRequest(floor, this))
                {
                    sm.setRiseOrSinkState(floor);
                    return true;
                }
                return false;
            }


            override public bool handleUnHalt(ElevSM sm) { return base.handleUnHalt(sm); }

            override public void begin(ElevSM sm) { sm.mElevator.waitAt(); }
            override public void update(ElevSM sm) { sm.updateWaitingState(); }
            override public String ToString() { return "WAIT"; }
        }

        private class RisingState : State
        {
            override public void begin(ElevSM sm)
            {
                Elevator elev = sm.mElevator;
                elev.mEtaNextFloorMs = sm.mTimeLastStateChangeMs + elev.mTimeToRiseOneFloorMs;
            }
            override public void update(ElevSM sm) { sm.updateRisingState(); }
            override public String ToString() { return "RISE"; }
        }

        private class SinkingState : State
        {
            override public void begin(ElevSM sm)
            {
                Elevator elev = sm.mElevator;
                elev.mEtaNextFloorMs = sm.mTimeLastStateChangeMs + elev.mTimeToSinkOneFloorMs;
            }
            override public void update(ElevSM sm) { sm.updateSinkingState(); }
            override public String ToString() { return "SINK"; }
        }

        private class HaltedState : State
        {
            /** Already in HaltedState, so do nothing! */
            override public bool handleHalt(ElevSM sm)
            {
                sm.mElevator.reportHaltRequestHandled(RequestType.HALT_AT, false, this);
                return false;
            }
            override public void update(ElevSM sm) { sm.updateHaltState(); }
            override public String ToString() { return "HALT"; }
        }


        /***************************************************************************/
        /* State Machine helper methods, called by the States' request handlers.   */
        /***************************************************************************/

        // Helper method, currently used by only the WaitingState
        private void setRiseOrSinkState(int floor)
        {
            if (floor > mElevator.getFloorNow())
                setState(mRisingState);
            else
                setState(mSinkingState);
        }

        /***************************************************************************/
        /* State Machine request handlers, called by the States' request handlers. */
        /***************************************************************************/

        private bool haltTheElevator(State handlerState)
        {
            bool elevatorHalted = mElevator.haltNow(handlerState);
            Debug.Assert(elevatorHalted == true);   // Only handle success for now.
            Debug.Assert(mState != mHaltedState);   // Never call this when already halted.
            mSaved = mState;
            setState(mHaltedState);
            if (mVerbose > 0)
                Sx.format("{0} Halt at {1}\n", getState(), mElevator.getFloorNow());
            return elevatorHalted;
        }

        bool unHaltTheElevator(State handlerState)
        {
            bool elevatorUnHalted = mElevator.unHalt(handlerState);
            Debug.Assert(mState == mHaltedState);
            Debug.Assert(mSaved != mHaltedState);
            setState(mSaved);            // Restore state prior to halt
            return elevatorUnHalted;
        }        

        /************************************************************************/
        /* Elevator request handlers, called by the States' request handlers.   */
        /************************************************************************/

        // If the elevator is already stopped at a floor (Waiting or Halted),
        // don't even try to add a stop for that same floor.  But if the elevator
        // is moving, try to add the stop even if the elevator just happens to be
        // "at" that floor already (i.e. it's passing by that floor with the doors
        // closed).  If the elevator has enough lead time to decelerate and stop,
        // it will.  Otherwise, it will stop at that floor later.
        // TODO: Model doors open/closed.  Then a stop-request for the current
        // floor resolves into an open-doors request.  (Use cases: person(s) enter
        // elevator, but nobody presses a (different-floor) stop button, or else
        // somebody does press the close-door button, or somebody on another 
        // floor presses a call-UP or call-DOWN button, the the doors begin to 
        // close.  A person on the elevator decides he wants off, presses stop
        // button for current floor.  The RTTD is to open the doors.
        internal bool handleStopRequest(int floor, IState handlerState)
        {
            bool added = false;
            if (floor != mElevator.getFloorNow() || mState == mRisingState || mState == mSinkingState)
            {
                added = mElevator.addStopRequest(floor);
            }
            if (mVerbose > 1)
            {
                mElevator.reportRequestHandled(RequestType.STOP_AT, floor, added, handlerState);
            }
            return added;
        }

        internal bool handleCallUpRequest(int floor, IState handlerState)
        {
            // Special case: there should be no Call-UP request on the top floor,
            // and in fact the wall on the top floor should have no such button.
            bool added = false;
            Debug.Assert(mElevator.mMinFloor <= floor && floor <= mElevator.mMaxFloor);
            if (mElevator.getFloorNow() != floor || mState == mRisingState || mState == mSinkingState)
            {
                added = mElevator.addCallUpRequest(floor);
            }
            if (mVerbose > 1)
            {
                mElevator.reportRequestHandled(RequestType.CALL_UP, floor, added, handlerState);
            }
            return added;
        }

        internal bool handleCallDownRequest(int floor, IState handlerState)
        {
            // Special case: there should be no Call-DOWN request on the bottom floor,
            // and in fact the wall on the bottom floor should have no such button.
            bool added = false;
            Debug.Assert(mElevator.mMinFloor <= floor && floor <= mElevator.mMaxFloor);
            if (mElevator.getFloorNow() != floor || mState == mRisingState || mState == mSinkingState)
            {
                added = mElevator.addCallDownRequest(floor);
            }
            if (mVerbose > 1)
            {
                mElevator.reportRequestHandled(RequestType.CALL_DN, floor, added, handlerState);
            }
            return added;
        }

        /**************************************************************************/
        /* Elevator updateStates methods, may be called from within State updates */
        /**************************************************************************/

        internal void updateWaitingState()
        {
            // Do not sleep here!  Only the main event loop can sleep, otherwise we
            // risk that loop calling this again while it still sleeps.
            if (mUseEarlyDecision)
            {
                if (mElevator.nextUpStop() > mElevator.getFloorNow())       // prioritize UP when waking up
                    setState(mRisingState);
                else if (mElevator.nextDownStop() < mElevator.getFloorNow())
                    setState(mSinkingState);
            }
            else
            {
                int up = mElevator.numReqsUp();
                int dn = mElevator.numReqsDown();
                if (up > dn)
                    setState(mRisingState);
                else if (dn > 0)
                    setState(mSinkingState);
            }
        }

        void updateRisingState()
        {
            if (timeMs() >= mElevator.mEtaNextFloorMs)
            {
                mElevator.moveUp();
            }
        }

        void updateSinkingState()
        {
            if (timeMs() >= mElevator.mEtaNextFloorMs)
            {
                mElevator.moveDown();
            }
        }

        void updateHaltState()
        {
            // Do not sleep here!  Only the main event loop can sleep, otherwise we
            // risk that loop calling this again while it still sleeps.
        }




        /************************************************************************/
        /* Test methods and unit tests                                          */
        /************************************************************************/

        /** Test method: Generates a sequence of random requests to the elevator.
         *  The sequence will contain one Halt, and later, one unHalt. 
         */
        private static int generateRandomRequests(Elevator elevator, Random rng, IList<Request> requests, int numRequests, int endReqIdx)
        {
            Debug.Assert(numRequests > 9);
            int floor, reqIdx;
            int jHalt = rng.Next(numRequests / 2);
            int jUnHalt = jHalt + 2 + rng.Next(numRequests / 3);
            for (int j = 0; j < numRequests; j++)
            {
                floor = rng.Next(elevator.mNumFloors);
                if (j == jHalt)
                {
                    reqIdx = (int) RequestType.HALT_AT;
                }
                else if (j == jUnHalt)
                {
                    reqIdx = (int) RequestType.UN_HALT;
                }
                else
                {
                    reqIdx = rng.Next(endReqIdx);
                }
                RequestType requestType = (RequestType) reqIdx;
                requests.Add(new Request(requestType, floor));
            }
            if (elevator.mVerbose > 1)
            {
                Sx.format("Generated {0} random requests for testing.\n", numRequests);
            }
            return numRequests;
        }

        /** Test method: Sends a sequence of requests to the elevator, each delayed randomly. */
        private static int sendRandomlyDelayedRequests(ElevSM elevSM, Random rng, IList<Request> randomReqs, int endRequestIdx)
        {
            int baseTime = 357, totalTime = 0;
            int maxTimeBetween = elevSM.mElevator.mMaxInterReqTimeMs;
            foreach (Request request in randomReqs)
            {
                int randTime = baseTime + rng.Next(maxTimeBetween);
                Threads.tryToSleep(randTime);
                sendRequest(elevSM, request);
                totalTime += randTime;
            }
            return totalTime;
        }

        /** Test method: Sends a single request to the elevator without delay. */
        private static bool sendRequest(ElevSM elevSM, Request request)
        {
            if (elevSM.mVerbose > 1)
            {
                var reqType = request.mType;
                var floor   = request.mFloor;
                Sx.puts("     Send Request:  " + reqType + (reqType < RequestType.HALT_AT ? (" " + floor) : ""));
            }
            return elevSM.receiveRequest(request);
        }

        /** Test method: Receives a request passes it to the current state for handling. */
        bool receiveRequest(Request request)
        {
            RequestType reqType = request.mType;
            int floor = request.mFloor;
            if (mVerbose > 1)
            {
                Sx.puts("     Recv Request:  " + reqType +
                    (reqType < RequestType.HALT_AT ? (" " + floor) : ""));
            }
            if (floor < 0 || floor >= mElevator.mNumFloors)
                return false;
            switch (reqType)
            {
                case RequestType.STOP_AT:
                    return mState.handleStopAt(this, floor);
                case RequestType.CALL_UP:
                    return mState.handleCallUpAt(this, floor);
                case RequestType.CALL_DN:
                    return mState.handleCallDownAt(this, floor);
                case RequestType.HALT_AT:
                    return mState.handleHalt(this);
                case RequestType.UN_HALT:
                    return mState.handleUnHalt(this);
            }
            return false;
        }


        /**************************************************************************/
        /* Update Loops: Run state-machine in a timer or other thread, or in main */
        /**************************************************************************/

        /************************************************************************
         * Use this to run the update loop using a timer.
         * Its behavior is similar to Java's TimerTask, but in C#, Timer is sealed, 
         * so we compose with TImer rather than inherit from it.
        Usage:
        <br><code>
           mTimer = new Timer();
           <br>
           mTimer.schedule(new UpdateTask(this), 0, mPeriodMs);
        </code>
        */
        class UpdateTimer           // Like Java TimerTask 
        {
            private readonly System.Threading.Timer mTimer;
            private readonly ElevSM mElevSM;

            public UpdateTimer(ElevSM elevSM)
            {
                mElevSM = elevSM;
                Elevator elevator = mElevSM.mElevator;
                TimerCallback tcb = mElevSM.update;
                mTimer = new System.Threading.Timer(tcb, null, elevator.mStartMs, elevator.mUpdatePeriodMs);
            }

            public void run()
            {
                mElevSM.mState.update(mElevSM);
            }

            // Specify what you want to happen when the Elapsed event is raised.
            //private static void OnTimedEvent(object source, ElapsedEventArgs e)
            //{
            // callActivity();
            //}
        }


        /**
         * Run using updates in a timer
         */
        void startUsingTimerThread()
        {
            if (mUpdateTimer != null)
                return;
            mUpdateTimer = new UpdateTimer(this);
        }


        /**
         * Use this method to run the update loop in a thread w/o a timer.
         * Usage:
         * new Thread(new Runnable() {
                public void run() { runInThread(); }
            }).start();
         */
        public void runInThread()
        {
            /*
            Thread thread = Thread.currentThread();
            while ( ! thread.isInterrupted() && mvbRunInThread) {
                mState.update(this);
            }
            bool inted = thread.isInterrupted();
            Sx.puts("runInThread was interrupted? " + inted + "; ending...");
            */
        }

        void startUsingOtherThread()
        {
            Sx.puts("startUsingOtherThread: THIS IS JUST A STUB");
            /*
            mvbRunInThread  = true;
            mThread = new Thread(new Runnable() {
                public void run() { runInThread(); }
            });
            mThread.start();
             */
        }

        /** 
         * Cancels all update tasks and kills the timer;
         * Not safe for normal operation.  This turns the 
         * state machine off, as if an actual machine were powered off.
         */
        protected void finish()
        {
            Sx.format("{0} {1}: Finished with {2} up requests and {3} down requests unserviced.\n"
                , typeof(Elevator).Name, mElevator.mName, mElevator.numReqsUp(), mElevator.numReqsDown());


            /*
            if (mTimer != null)
            {
                mTimer.cancel();
                mTimer = null;
            }
            if (mThread != null)
            {
                mThread.interrupt();
                mvbRunInThread = false;
                ////mThread.stop();     // TODO: How to make it die gracefully?
                mThread = null;
            }
            */
        }


        protected static int test_usingMainThread(string testName, ElevSM elevSM, Random rng, IList<Request> randomReqs, int endRequestIdx)
        {
            Elevator elevator = elevSM.mElevator;
            Sx.format("{0}:  Starting State Machine in Main Thread: BEGIN\n", testName);
            int numReqs = randomReqs.Count;
            Sx.format("{0}:  Single-threaded event loop will interleave {1} requests: BEGIN\n", testName, numReqs);
            int totalUpdates = 0;
            for (int j = 0, end = randomReqs.Count; j < end; j++)
            {
                int numUpdatesBeforeNextRequest = rng.Next(7);
                for (int k = 0; k < numUpdatesBeforeNextRequest; k++)
                {
                    elevSM.update(elevSM);
                }
                ElevSM.sendRequest(elevSM, randomReqs[j]);
                totalUpdates += numUpdatesBeforeNextRequest;
            }
            Sx.format("{0} Single-threaded event loop: all {1} requests SENT among {1} updates\n", testName, numReqs, totalUpdates);
            int numUpdatesAfterLastRequest = elevator.mNumFloors * 3;
            for (int k = 0; k < numUpdatesAfterLastRequest; k++)
            {
                elevSM.update(elevSM);
            }
            Sx.format("{0} end of function in main thread. after {1} more updates, {2} total\n", testName, numUpdatesAfterLastRequest, totalUpdates);
            return 0;
        }

        protected static int test_usingOtherThread(string testName, ElevSM elevSM, Random rng, IList<Request> randomReqs, int endRequestIdx)
        {
            elevSM.startUsingOtherThread();
            return sendRandomlyDelayedRequests(elevSM, rng, randomReqs, endRequestIdx);
        }

        protected static int test_usingTimerThread(string testName, ElevSM elevSM, Random rng, IList<Request> randomReqs, int endRequestIdx)
        {
            Sx.format("{0}:  Starting State Machine in a Timer Thread: BEGIN\n", testName);
            elevSM.startUsingTimerThread();
            int numReqs = randomReqs.Count;
            Sx.format("{0}  main thread function to send {1} requests: BEGIN\n", testName, numReqs);
            int millis = sendRandomlyDelayedRequests(elevSM, rng, randomReqs, endRequestIdx);
            Sx.format("{0} finished sending; all {1} requests SENT in {2:0.0} seconds\n", testName, numReqs, millis / 1000.0);
            // Sleep to let the SM thread spin a while longer (after all updates, it should WAIT).
            int sleepWaitMs = 9876;
            double sleepWaitS = sleepWaitMs / 1000.0;
            Sx.format("{0} sleep {1:0.0} seconds before ending timer thread: BEGIN . . .\n", testName, sleepWaitS);
            Threads.tryToSleep(sleepWaitMs);
            Sx.format("{0} slept {1:0.0} seconds; timer thread will now die: . . . END\n", testName, sleepWaitS);
            return 0;
        }

        public static int unit_test(int level, string[] args)
        {
            int result = 0;
            int numRequests = 24;
            String testName = typeof(Elevator).Name + ".unit_test";
            Sx.format("{0} main thread function to send {1} requests: BEGIN\n", testName, numRequests);

            if (level > 0)
            {
                Random rng = new Random(numRequests);
                int minFloor = 0, maxFloor = 9;
                int periodMs = 11;
                if (args.Length > 0)
                    periodMs = 5;

                Elevator elevator = new Elevator("El Ten", minFloor, maxFloor, periodMs);
                elevator.mVerbose = 3;
                IList<Request> randomReqs = new List<Request>();
                int endRequestIdx = Enum.GetNames(typeof(RequestType)).Length - 2;
                generateRandomRequests(elevator, rng, randomReqs, numRequests, endRequestIdx);
                ElevSM elevSM = elevator.getSM();
                elevSM.setWaitingState();


                if (level == 1)
                    test_usingMainThread(testName, elevSM, rng, randomReqs, endRequestIdx);
                else if (level == 2)
                    test_usingOtherThread(testName, elevSM, rng, randomReqs, endRequestIdx);
                else
                    test_usingTimerThread(testName, elevSM, rng, randomReqs, endRequestIdx);
                elevSM.finish();
            }
            return result;
        }

    }
}
