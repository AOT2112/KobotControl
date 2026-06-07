# KobotControl

KobotControl software is designed for controlling Kobot robot manipulator according to the script specified in the configuration file (cfg.json). This software interacts with the low-level software "CAN Master", which is installed on the same workstation as the software "KobotControl". To control the manipulator, the KobotControl software sends the control mode and target positions of the manipulator drives to CAN Master software using the UDP protocol. The data sent to the CAN Master is encoded in JSON format. The feedback received from the "CAN Master" software is transmitted in the same way. It includes the states and parameters of each manipulator drive (current, speed, position).
Each manipulator drive is controlled in position control mode.

IP address of the second standalone robot manipulator: 192.168.10.2; port: 5900.

The KobotControl software has the following set of commands to control the manipulator using a script:
| Command name | Command description| Command parameters|
| ------ | ------ | ------ |
| MOTION | Performs simultaneous motion of several manipulator drives in accordance with defined parameters| An array of splines, waiting time for the completion of the all manipulator drives motion in milliseconds when executing this command (timeoutMs). Each spline is a dictionary. The key of the dictionary is name of the manipulator drive. The dictionary values are the parameters: target position of the manipulator drive (Position), delay in milliseconds before the start of drive motion (DelayMS)|
| GRIP | Grip control according to the set parameters| Grip position (Position); grip force (Force); time to control the grip position in milliseconds (TimeMS)|
| PAUSE | Delay in executing the manipulator control script| Delay time in milliseconds (TimeMS)|
| ANOTHER | Launching a second standalone robot manipulator. This command is waiting for completion of the control script of second manipulator.| Assembly line number of the second manipulator (LineNumber)|
| GOTO | Going to the specific command number of the manipulator control script| The number of script command (Index), the minimum value of this parameter is 0|

The default configuration file is located in the root directory of this repository and is named "DefaultConfig.json". The ready working configuration file is located in the directory: "KOBOT Control\bin\x64\Release" and is named "cfg.json".
