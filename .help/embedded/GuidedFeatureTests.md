# Guided Feature Tests

Guided feature tests cover many aspects of the TiXL editor and help us test in depth before shipping new releases.
But they are also great as an interactive tour through all details of a feature.

## Starting a Test Run

1. Open the window from TiXL -> Development Tools -> Guided Feature Tests
2. Select one or more Test Sets 
3. Click _Start_

## How to test

1. Each step asks you to perform a small action, like clicking a button or creating an operator.
2. Once you did this, you can compare your result to the expected results:
  - If they match: Great! Everything is working as expected -> Click **Success** to jump to the next step
  - If things don't work, add a short comment and press **Fail**.
  - If you don't understand the Actions or can't figure out how to do the actions, please add a short comment and press **Other**.

## After testing

Please export the test as JSON or post a screenshot. In the long run we hope to integrate a server solution to share the test results with the development team.
