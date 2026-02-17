#!/usr/bin/env bash

# Print AppHost PID(s) and command line to help pick the correct process in the attach dialog.
ps -eo pid,cmd | grep "deepwiki-open-dotnet.AppHost" | grep -v grep || echo "No running AppHost process found."
