# Project Overview  
>This is a prototype program to demonstrate our implementation of the [Raft Consensus algorithm](https://raft.github.io) ([from the Paper](https://raft.github.io/raft.pdf)).  
>Currently our implementation includes the full list of Raft algorithm features described in the paper  
>This prototype utilises our own [Nuget package](https://www.nuget.org/packages/TeamDecided.RaftConsensus/) based on our [library](https://bitbucket.org/teamdecided/raftconsensuslibrary/src/master/)  
>In our terminology, this prototype is a functional "UAS" (User Application Service)  
>This prototype demonstrates the library's functionality by using our generic key value store interface IConsensus<TKey, TValue> with <string,string> as an example. It then maintains a distributed log accross multiple nodes following the Raft algorithm's protocol, including sustaining and recovering from minority node failure while keeping service active.  
>This library uses our own UDP networking library, including an optional encryption layer based on [System.Security.Cryptography](https://docs.microsoft.com/en-us/dotnet/standard/security/cryptography-model)  
>We've incldued an easy to use bootstrapper which allows easily spinning up the multiple Raft nodes on the same PC  

# Authors  
>[Joshua](https://bitbucket.org/JoshuaMichael/)  
>[Sean](https://bitbucket.org/neas57/)  

# Screenshots  

## Bootstrapper  
![RaftPrototype Windows screenshot](https://cdn.discordapp.com/attachments/428466046391812098/454992491599626261/unknown.png)  
## 3 nodes with distributed log  
![RaftNode Windows screenshot](https://cdn.discordapp.com/attachments/428466046391812098/454992817521950731/unknown.png)  
## 3 nodes with distributed log, showing debug log  
![Raft Node Debug Log Windows screenshot](https://cdn.discordapp.com/attachments/428466046391812098/454992979422347265/unknown.png)  