# Background
In any large-scale Windows environment, there is likely a re-occurring need to collect, analyze, and react to host-based data whether it be for IT operations, cyber security response, or cyber research.  Typically, these needs are met through the development of one-off scripts, utilities, and single-purpose agents.  One problem with this approach is scalability.  As more data collection needs arise, more and more one-off scripts (and the like) are deployed to endpoints slowly increasing the combined resource load and associated “agent bloat”.  

In addition, because Windows is a complex and modular operating system, a myriad of APIs exist with little consistency in how they describe the data they provide.   For example, a process might be identified by name from one API, by PID from another, and by a collection of thread IDs from a third.  A user may be identified by SID, by Active Directory GUID, or by a username.  A file might be identified by a logical path, by a physical path or by one of the other 17 observed OS path variants.  In such an ecosystem, the one-off approach to host-based data collection creates datasets that, while useful for a specific need, likely lack any chance for reusability or discoverability. 

<img width="628" alt="Wintap OSS Readme Graphic" src="https://user-images.githubusercontent.com/50601643/180505875-ff160e04-e0f0-4172-a9a6-d40d92e869c6.PNG">

Wintap aspires to solve these problems by providing the following capabilities:

•	A singular and extensible service-based runtime environment.  Wintap provides an easy-to-use extensibility API so that new functionality can be merged through the development of independent library modules while maintaining a single agent footprint on the endpoint.

•	A unified data model.  Wintap provides a single, strongly typed data model for describing data sourced from the myriad of underlying APIs it consumes from.  This unified model forms a foundation upon which data can be consistently discovered, consumed, combined, and correlated.

•	API abstraction. Wintap integrates deeply into many low-level Windows event streams such as Event Tracing for Windows (ETW), COM+ System Notifications and others.  Plugin authors can easily take advantage of these rich event sources without needing to implement any of the low-level API details.

•	Data discovery.  Wintap provides an integrated, locally hosted web-based analytic “workbench” from where real-time event streams can be queried and explored. 

# Minimum System Requirements
Desktop OS:  Windows 10 64-bit

Server OS: Windows Server 2016 64-bit

.NET Framework: 4.8

Memory: 4GB


# Release
LLNL-CODE-837816
