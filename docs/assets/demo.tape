Output docs/assets/demo.gif

Set Shell "zsh"
Set Width 1000
Set Height 620
Set FontSize 12
Set LineHeight 1.1
Set Padding 16
Set Theme "Catppuccin Mocha"
Set Framerate 15
Set TypingSpeed 10ms
Set CursorBlink false

Sleep 1s

Type `npx --yes @modelcontextprotocol/inspector --cli dotnet samples/IncidentSandbox/bin/Debug/net10.0/IncidentSandbox.dll --method tools/call --tool-name get_service_health --tool-arg service=auth --format json 2>/dev/null | jq '.result.structuredContent | {summary,diagnostics,data:[.data[]|{service,status,critical_count,error_count,active_ticket_count}],suggested_next_steps}'`
Enter
Sleep 8s

Type `npx --yes @modelcontextprotocol/inspector --cli dotnet samples/IncidentSandbox/bin/Debug/net10.0/IncidentSandbox.dll --method tools/call --tool-name search_tickets --tool-arg 'query=log in' status=open service=auth --format json 2>/dev/null | jq '.result.structuredContent | {summary,diagnostics,data:[.data[]|{id,title,status,services}],suggested_next_steps}'`
Enter
Sleep 9s

Type `npx --yes @modelcontextprotocol/inspector --cli dotnet samples/IncidentSandbox/bin/Debug/net10.0/IncidentSandbox.dll --method tools/call --tool-name correlate_incident --tool-arg ticketId=TCK-1028 --format json 2>/dev/null | jq '.result.structuredContent | {summary,diagnostics,data:{ticket:(.data.ticket|{id,title,status}),events:(.data.events|unique_by(.message)|map(select(.message|test("expired|502")))|map({service,level,message}))},suggested_next_steps}'`
Enter
Sleep 9s
