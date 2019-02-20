#!/bin/sh

trap "echo TRAPed signal" HUP INT QUIT TERM

/usr/bin/dotnet SimulatedTemperatureSensor.dll

echo "[hit enter key to exit] or run 'docker stop <container>'"
read

echo "exited $0"
