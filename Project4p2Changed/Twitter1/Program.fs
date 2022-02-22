//#r "bin/Debug/netcoreapp3.1/Akka.dll"
//#r "bin/Debug/netcoreapp3.1/Akka.FSharp.dll"
//#r "bin/Debug/netcoreapp3.1/Akka.dll"
//#r "bin/Debug/netcoreapp3.1/Akka.FSharp.dll"
 
//#time "on"
 

module Suave

open Suave
open Suave.Http
open Suave.Operators
open Suave.Filters
open Suave.Successful
open Suave.Files
open Suave.RequestErrors
open Suave.Logging
open Suave.Utils
open System
open Akka
open Akka.Actor
open System.Collections.Generic
open Akka.FSharp
open System.Security.Cryptography
open System.Net
open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket
open System.Threading

 
let mutable Users = 500
 
// if Users < 500 then
//     printfn "Input Less than 500, taking 500 as default!"
//     Users <- 500
 
 
type tweeetdb= struct
    val tweetId: int
    val tweetMessage: String
    val userId: int
    val ifRetweet: bool
    val retweetId: int
    val retweetedByUser: int
 
    new (tid,msg,uid,isR,rtid,ruid) = {tweetId= tid;tweetMessage = msg;userId = uid;ifRetweet = isR; retweetId = rtid;retweetedByUser = ruid}
end

type userloginDb = struct
    val userId: int
    val FirstName: String
    val LastName: String
    val username: String
    val Email: String
    val Password: String
  
    new (uid,fn,ln,un,email,pwd) = {userId= uid;FirstName = fn; LastName = ln; username = un;Email = email;Password =pwd}
end
type userInterface =
    | InitializeClient of int
    | FollowersAndFollowingAdded of Set<int>*int
    | AddOneFollower_Following of int*int 
    | TweetRegisterOnEngine of String
    | RetweetRegisterOnEngine of int
    | StoreNewTweets of tweeetdb
    | TweetRegistered of tweeetdb
    | SearchTweet of String
    | Display of Set<tweeetdb>
    | SignIn
    | SignOut
    | UpdateHomePage of Set<tweeetdb>
    | GenerateRandomTweetId
    | DisplayUserProfile of int
    | DisplayHomePage
    | InitializeHome
    | AddFollowerNew of int
    | UpdateFollowingNew of int
type userGenerate =
    | CreateUsers of int

// Simulator is going to make connections between the clients using zipf distribution and is going to randomly populate followers/following
// Simulator has a datastorage map for following and followers
// Simulator begins the engine and calls the function to spawn the clients    
type simulatorInterface =
    | InitializeSimulator of int
    | Mapping of Map<int,IActorRef>
    | ConnectUsers
    | BeginTweets of String*int
    | BeginRetweets of int*int
    | CountAllTweetsByUsers of int
    | BeginTweetsForAllUsers of String
    | BeginRetweetsForAllUsers
    | SwitchState of int
    | DisplayUserHome of int
    | Reset
    | DisplayFollower of int
    | DisplayFollowing of int
    | DisplayFollowerServer of int
    | DisplayFollowingServer of int
    | SimulateSearch of String*int
 
//twitter has a datastorage map for following and followers that gets populated from the simulator
//twitter enginer registers the clients for sign in/sign up
//twitter finds tags 
type twitterEngineInterface=
    | BeginEngine of int
    | SignUp of int*IActorRef
    | CompleteUserProfile
    | SimulatorsResponse of Map<int,Set<int>>*Map<int,Set<int>>
    | StoreNewTweet of String*int
    | RegisterReTweet of int*int
    | Search of String
    | ChangeServerState of int
    | DisplayFollowersOfUsers of int
    | DisplayFollowingofUSers of int
    | Register4 of int
    | UpdateFollow of int*int
 
 
type tweetInterface = struct
    val tweetId: int
    val userId: int
    val tweetMessage: String
 
    new (tid,uid,msg) = {tweetId = tid;  userId = uid;tweetMessage = msg;}
 
end
 
type retweetInterface = struct
    val retweetId: int
    val tweetId: int
    val retweetedByUser: int
    val tweetMessage : String
    new (rtid,tid,uid,msg) = {retweetId = rtid;tweetId = tid;retweetedByUser = uid;tweetMessage = msg;}
end

let mutable socketMap = Map.empty<int,WebSocket>
let mutable globalusermap = Map.empty<int,IActorRef>
let mutable actorrefmap = Map.empty<int,IActorRef>
let mutable userDetailDb = Map.empty<String,userloginDb>
let arbitrary = System.Random() 

let ws (webSocket : WebSocket) (context: HttpContext) =
  socket {
    let mutable loop = true
    while loop do
      let! msg = webSocket.read()
      match msg with
      | (Ping, data, false) ->
        printfn "true"
      | (Text, data, true) ->
        let str = UTF8.toString data
        let mutable response = sprintf "%s" str
        
        if (response.Contains "SIGNUP") then 
            let result = response.Split '&'
            let id = Users + 1
            Users <- Users + 1
            let fname = (result.[1].Split ':').[1]
            let lname = (result.[2].Split ':').[1]
            let email = (result.[3].Split ':').[1]
            let pwd = (result.[4].Split ':').[1]
            let un = fname + lname
            printfn "%s signep up." un
            userDetailDb <- userDetailDb.Add(un, new userloginDb(id,fname,lname,un,email,pwd))
            actorrefmap.[0] <! Register4 id
            response <- "Signup Successful"

        if (response.Contains "LOGIN") then
            let result = response.Split '&'
            let uname = (result.[1].Split ':').[1]
            let pwd = (result.[2].Split ':').[1]
            if (userDetailDb.[uname].Password = pwd) then
                let myid = userDetailDb.[uname].userId
                response <- "Login Succesful"
                printfn "User%i Logged in!" myid
                globalusermap.[myid] <! SignIn
            else
                response <- "Wrong Credentials Provided By the User!"
            
        if (response.Contains "HOME") then 
            let result = response.Split '&'
            let uname = (result.[1].Split ':').[1]
            let myid = userDetailDb.[uname].userId
            socketMap <- socketMap.Add(myid,webSocket)
            globalusermap.[myid] <! InitializeHome // Client actor

        if (response.Contains "TWEET") then 
            let result = response.Split '&'
            let uname = (result.[1].Split ':').[1]
            let tweet = (result.[2].Split ':').[1]
            let myid = userDetailDb.[uname].userId
            printfn "userId %i Made a Tweet" myid 

            globalusermap.[myid] <! TweetRegisterOnEngine tweet

        if (response.Contains "RTWT") then 
            let result = response.Split '&'
            let uname = (result.[1].Split ':').[1]
            let tweetid = (result.[2].Split ':').[1] |>int
            let myid = userDetailDb.[uname].userId
            globalusermap.[myid] <! RetweetRegisterOnEngine tweetid

        if (response.Contains "SEARCH") then 
            let result = response.Split '&'
            let uname = (result.[1].Split ':').[1]
            let searchtext = (result.[2].Split ':').[1] 
            let myid = userDetailDb.[uname].userId
            printfn "userId %i Made a Search " myid 
            globalusermap.[myid] <! SearchTweet searchtext

        if (response.Contains "FOLLOWXYZ") then 
            let result = response.Split '&'
            let uname = (result.[1].Split ':').[1]
            let tobefollowed = (result.[2].Split ':').[1] 
            let myid = userDetailDb.[uname].userId
            let id = userDetailDb.[tobefollowed].userId
            globalusermap.[myid] <! AddFollowerNew id

        if (response.Contains "LOGOUT") then
            let result = response.Split '&'
            let uname = (result.[1].Split ':').[1]
            printfn "%s logged out!" uname
            let myid = userDetailDb.[uname].userId
            globalusermap.[myid] <! SignOut


        let byteResponse =
          response
          |> System.Text.Encoding.ASCII.GetBytes
          |> ByteSegment


        // the `send` function sends a message back to the client
        do! webSocket.send Text byteResponse true
      | (Close, _, _) ->
        let emptyResponse = [||] |> ByteSegment
        do! webSocket.send Close emptyResponse true

        // after sending a Close message, stop the loop
        loop <- false

      | _ -> ()
    }



/// An example of explictly fetching websocket errors and handling them in your codebase.
let wsWithErrorHandling (webSocket : WebSocket) (context: HttpContext) = 
   let exampleDisposableResource = { new IDisposable with member __.Dispose() = printfn "Resource needed by websocket connection disposed" }
   let websocketWorkflow = ws webSocket context
   async {
    let! successOrError = websocketWorkflow
    match successOrError with
    // Success case
    | Choice1Of2() -> ()
    // Error case
    | Choice2Of2(error) ->
        // Example error handling logic here
        printfn "Error: [%A]" error
        exampleDisposableResource.Dispose()
    return successOrError
   }



let app : WebPart = 
  choose [
    path "/websocket" >=> handShake ws
    path "/websocketWithSubprotocol" >=> handShakeWithSubprotocol (chooseSubprotocol "test") ws
    path "/websocketWithError" >=> handShake wsWithErrorHandling
    GET >=> choose [ path "/" >=> file "index.html"]
    GET >=> choose [ path "/signup" >=> file "signup.html"]
    GET >=> choose [ path "/login" >=> file "login.html"]
    GET >=> choose [ path "/home" >=> file "home.html"]
    NOT_FOUND "Found no handlers." ]

[<EntryPoint>]
let main _ = 
     
    let system = System.create "system" (Configuration.defaultConfig())   
    if Users < 500 then
        printfn "Input Less than 500, taking 500 as default!"
        Users <- 500
    let tweetList = new List<String>()
    for i = 1 to 20 do
        tweetList.Add(sprintf "#tweet%i @USer%i @KHIGH Likes BTS, Bangtan Sonyneondan #BTSArmy #HYBE" i i )
    
    let mutable engine = null
    let mutable spawnUser = null
    let mutable callSimulator = null
    let mutable flag= false
    let mutable random = 2
    let sha = SHA256.Create()
    let mutable TotalfollowersCount = 0
    let mutable StopIter = 0

    let user (mailbox : Actor<_>) =
        let mutable Id = 0
        let mutable active = true
        let mutable followers = Set.empty<int>
        let mutable following = Set.empty<int>
        let mutable homePage = new List<tweeetdb>()
        let mutable profilePage = new List<tweeetdb>()
        let rec loop() = actor {
            let! msg = mailbox.Receive()
            match msg with
            | InitializeClient (id)->
                Id <- id
            | SignIn ->
                engine <! ChangeServerState Id
                active <- true
            | SignOut ->
                engine <! ChangeServerState Id
                active <- false            
            | AddOneFollower_Following (id,x) ->
                if x = 1 then
                    followers <- followers.Add(id)
                else if x =2 then
                    following <- following.Add(id)

            | FollowersAndFollowingAdded (setofusers,x) ->
                if x = 1 then  
                    followers <- setofusers
                else if x = 2 then 
                    following <- setofusers
            | TweetRegisterOnEngine (tweet)->
                engine <! StoreNewTweet (tweet,Id)
            | RetweetRegisterOnEngine (tweetId) ->
                engine <! RegisterReTweet (tweetId,Id)
            | StoreNewTweets tweet ->
                if tweet.ifRetweet then
                    callSimulator <! CountAllTweetsByUsers 2
                else
                    callSimulator <! CountAllTweetsByUsers 1
                homePage.Add(tweet)
                if (socketMap.ContainsKey(Id)) then
                    let hittweet (tweet:tweeetdb) = 
                        let tid = (sprintf "%i" tweet.tweetId)
                        let rtid = (sprintf "%i" tweet.retweetId)
                        let cid = (sprintf "User%i" tweet.userId)
                        let sid = (sprintf "User%i" tweet.retweetedByUser)
                        let isr = tweet.ifRetweet|>string
                        let mutable strmsg = "type:htxyz&tid:"+tid+"&text:"+tweet.tweetMessage+"&creator:"+cid+"&isrt:"+isr+ "&rtid:"+rtid+ "&sby:"+sid
                        strmsg
                    let strmsg = hittweet(tweet)
                    let byteResponse1 =
                        strmsg
                        |> System.Text.Encoding.ASCII.GetBytes
                        |> ByteSegment
                    
                    let sendmsg (webSocket : WebSocket)=  
                        webSocket.send Text byteResponse1 true
                    Async.RunSynchronously(sendmsg socketMap.[Id])
            | TweetRegistered (msg) ->
                profilePage.Add(msg)
            | SearchTweet item ->
                engine <! Search (item)
            | Display set ->
                // printfn "Total Search results found: %i" set.Count
                // printfn "Top 8 results are: "
                // printfn ""
                // let mutable i = 0
                // for tweet in set do
                //     if i <8 then
                //         i<- i+1
                //         printfn "Tweet id: User%i" tweet.tweetId
                //         printfn "Tweet message: %s" tweet.tweetMessage
                //         printfn "Tweet userid: %i" tweet.userId
                //         if tweet.ifRetweet then
                //             printfn "Retweet by User%i" tweet.retweetedByUser
                //         printfn ""
                printfn "Displaying Tweets Searched by User %i" Id
                let hittweet (tweet:tweeetdb) = 
                    let tid = (sprintf "%i" tweet.tweetId)
                    let rtid = (sprintf "%i" tweet.retweetId)
                    let cid = (sprintf "User%i" tweet.userId)
                    let sid = (sprintf "User%i" tweet.retweetedByUser)
                    let isr = tweet.ifRetweet|>string
                    let mutable str = "type:sxyz&tid:"+tid+"&text:"+tweet.tweetMessage+"&creator:"+cid+"&isrt:"+isr+ "&rtid:"+rtid+ "&sby:"+sid
                    str
                for i in set do 
                    let str = hittweet(i)
                    let byteResponse1 =
                        str
                        |> System.Text.Encoding.ASCII.GetBytes
                        |> ByteSegment
                    
                    let sendmsg (webSocket : WebSocket)=  
                        webSocket.send Text byteResponse1 true
                    Async.RunSynchronously(sendmsg socketMap.[Id])
            | UpdateHomePage tweets ->
                for tweet in tweets do
                    homePage.Add(tweet)
            | GenerateRandomTweetId ->
                let mutable id = arbitrary.Next(0,homePage.Count)
                mailbox.Self <! RetweetRegisterOnEngine homePage.[id].tweetId
            | DisplayUserProfile (x)->
                if (x= 1) then
                    for user in followers do
                        printf "%i " user
                    printfn ""
                    let list = new List<int>(followers)
                    let randomFollower = list.[arbitrary.Next(0,list.Count)]
                    printfn "Random follower Selected is %i" randomFollower
                    random <- randomFollower
                    StopIter <- StopIter + 1
                else if x = 2 then 
                    printfn "Client Following!"
                    for user in following do
                        printf "%i " user
                    printfn ""
        
            | DisplayHomePage ->
                printfn ""
                for tweet = homePage.Count-1 downto Math.Max(homePage.Count-6,0) do
                    printfn "Tweet id: User%i" homePage.[tweet].tweetId
                    printfn "Tweet message: %s" homePage.[tweet].tweetMessage
                    printfn "Tweet Created By: %i"  homePage.[tweet].userId
                    if homePage.[tweet].ifRetweet then
                        printfn "Retweet by: User%i" homePage.[tweet].retweetedByUser
                    printfn ""

            | InitializeHome ->
                let hittweet (tweet:tweeetdb) = 
                    let tid = (sprintf "%i" tweet.tweetId)
                    let rtid = (sprintf "%i" tweet.retweetId)
                    let cid = (sprintf "User%i" tweet.userId)
                    let sid = (sprintf "User%i" tweet.retweetedByUser)
                    let isr = tweet.ifRetweet|>string
                    let mutable strmsg = "type:hi4xyz&tid:"+tid+"&text:"+tweet.tweetMessage+"&creator:"+cid+"&isrt:"+isr+ "&rtid:"+rtid+ "&sby:"+sid
                    strmsg

                let makeuser (id: int)=
                    let mutable strmsg = "type:dfl&user:" + (id|>string)
                    strmsg
                
                let makeuser1 (id: int)=
                    let mutable strmsg = "type:dofl&user:" + (id|>string)
                    strmsg

                for i = 0 to homePage.Count - 1 do 
                    let strmsg = hittweet(homePage.[i])
                    let byteResponse1 =
                        strmsg
                        |> System.Text.Encoding.ASCII.GetBytes
                        |> ByteSegment
                    
                    let sendmessage (webSocket : WebSocket)=  
                        webSocket.send Text byteResponse1 true
                    Async.RunSynchronously(sendmessage socketMap.[Id])
                
                for i in followers do 
                    let strmsg = makeuser i
                    let byteResponse1 =
                        strmsg
                        |> System.Text.Encoding.ASCII.GetBytes
                        |> ByteSegment
                    
                    let sendmessage (webSocket : WebSocket)=  
                        webSocket.send Text byteResponse1 true
                    Async.RunSynchronously(sendmessage socketMap.[Id])

                for i in following do 
                    let strmsg = makeuser1 i
                    let byteResponse1 =
                        strmsg
                        |> System.Text.Encoding.ASCII.GetBytes
                        |> ByteSegment
                    
                    let sendmessage (webSocket : WebSocket)=  
                        webSocket.send Text byteResponse1 true
                    Async.RunSynchronously(sendmessage socketMap.[Id])
            | AddFollowerNew (tobefollowed) ->
                following <- following.Add(tobefollowed)
                engine <! UpdateFollow (tobefollowed,Id)

                let mutable strmsg = "type:dofl&user:" + (tobefollowed |>string)
                let byteResponse1 =
                    strmsg
                    |> System.Text.Encoding.ASCII.GetBytes
                    |> ByteSegment
                    
                let sendmessage (webSocket : WebSocket)=  
                    webSocket.send Text byteResponse1 true
                // printfn "afn Socket Map %A and id %i" socketMap Id
                Threading.Thread.Sleep(2000)
                Async.RunSynchronously(sendmessage socketMap.[Id])
            
            | UpdateFollowingNew (follower) ->
                // printfn "Follower Added!!"
                followers <- followers.Add(follower)
                let mutable strmsg = "type:dfl&user:" + (follower|>string)
                let byteResponse1 =
                    strmsg
                    |> System.Text.Encoding.ASCII.GetBytes
                    |> ByteSegment
                    
                let sendmessage (webSocket : WebSocket)=  
                    webSocket.send Text byteResponse1 true
                // printfn "ufn Socket Map %A and id %i" socketMap Id
                Threading.Thread.Sleep(2000)
                Async.RunSynchronously(sendmessage socketMap.[Id])
            
    
            return! loop()
        }
        loop()
    let usergenerator  (mailbox : Actor<_>) =
        let rec loop() = actor {
            let! msg = mailbox.Receive()
            match msg with
            | CreateUsers (numOfClients) ->
                for userId = 1 to numOfClients do
                    let client = spawn system (sprintf "User%i" userId) user
                    let name = sprintf "User%i" userId
                    let email = sprintf "User%i@twitter.com" userId
                    userDetailDb <- userDetailDb.Add(name,new userloginDb(userId,name,"",name,email,name))
                    client<! InitializeClient userId
                    globalusermap <- globalusermap.Add(userId,client)
                    engine <! SignUp (userId,client)
    
                engine <! CompleteUserProfile
            return! loop()
        }
        loop()
    

    

    
    let twitterEngine (mailbox : Actor<_>) =
        let mutable followerMap = Map.empty<int,Set<int>>
        let mutable followingMap = Map.empty<int,Set<int>>
        let mutable userMap = Map.empty<int,IActorRef>
        let mutable countTweets = 1
        let mutable countRetweets = 1
        let mutable tweetMap = Map.empty<int,tweetInterface>
        let mutable reTweetMap = Map.empty<int,retweetInterface>
        let mutable userTaggedMap = Map.empty<String,Set<int>>
        let mutable hashtagMap = Map.empty<String,Set<int>>
        let mutable textMap = Map.empty<String,Set<int>>
        let mutable ifUserActive = new List<bool>()
        let mutable pendingTweets = Map.empty<int,Set<tweeetdb>>
        let SeparateTweetContent (tweet:String) countTweets =
                    let SeparateTweetText (list:List<String>) x =
                        let mutable ref = Map.empty<String,Set<int>>
                        if x = 1 then
                            ref <- hashtagMap
                        else if x = 2 then
                            ref <- textMap
                        else
                            ref <- userTaggedMap
    
                        for text in list do
                            if (ref.ContainsKey(text)) then
                                ref <- ref.Add(text,ref.[text].Add(countTweets))
                            else
                                let set = Set.empty<int>.Add(countTweets)
                                ref <- ref.Add(text,set)
                        if x = 1 then
                            hashtagMap <- ref
                        else if x = 2 then
                            textMap <- ref
                        else
                            userTaggedMap <- ref
    
                    let hashtagList = new List<String>()
                    let userTaggedList = new List<String>()
                    let textList = new List<String>()
                    let splitTweet = tweet.Split ' '
            
                    for word in splitTweet do
                        if (word <> "") then
                            if(word.[0] = '#') then
                                hashtagList.Add(word)
                            else if(word.[0] = '@') then
                                userTaggedList.Add(word)
                            else
                                textList.Add(word)
                    SeparateTweetText hashtagList 1
                    SeparateTweetText userTaggedList 3
                    SeparateTweetText textList 2
    
        let updateTweets creator tweetStructure=
            let mutable list = followerMap.[creator]
            let mutable result = Set.empty<IActorRef>
    
            for addUser in list do
                if ifUserActive.[addUser] = true then
                    result <- result.Add(userMap.[addUser])
                else
                    pendingTweets <- pendingTweets.Add(addUser,pendingTweets.[addUser].Add(tweetStructure))
            result
    
        let rec loop() = actor {
            let! msg = mailbox.Receive()
            match msg with
            |BeginEngine (msg) ->
                let total = msg
                let mutable emptyset = Set.empty
                let mutable emptyset1 = Set.empty
                ifUserActive.Add(true)
                for id = 1 to total do
                    ifUserActive.Add(true)
                    pendingTweets <- pendingTweets.Add(id,emptyset1)
                    followerMap <- followerMap.Add(id,emptyset)
                    followingMap <- followingMap.Add(id,emptyset)

            |Register4 (uid) ->
                let client = spawn system (sprintf "User%i" uid) user
                client<! InitializeClient uid
                userMap <- userMap.Add(uid,client)
                let mutable set = Set.empty
                let mutable set1 = Set.empty
                followerMap <- followerMap.Add(uid,set)
                followingMap <- followingMap.Add(uid,set)
                pendingTweets <- pendingTweets.Add(uid,set1)
                globalusermap <- globalusermap.Add(uid,client)
                ifUserActive.Add(false)

            |SignUp (clientId,actorReference)->
                userMap <- userMap.Add(clientId,actorReference)
    
            | CompleteUserProfile ->
                callSimulator <! Mapping userMap
        
                
            |StoreNewTweet (message,user) ->
                
                SeparateTweetContent message countTweets
                tweetMap <- tweetMap.Add(countTweets,new tweetInterface(countTweets,user,message))
                let tweetStruct = new tweeetdb(countTweets,message,user,false,0,0)
                countTweets <- countTweets + 1      
                userMap.[user] <! TweetRegistered tweetStruct
                
                let followerSet = updateTweets user tweetStruct

                for i in followerSet do
                        i <! StoreNewTweets tweetStruct
                // callFanout <! Distribute (tweetStruct, fanoutSet)
    
            |RegisterReTweet (tweetId,userRetweeted) ->
                let message= tweetMap.[tweetId].tweetMessage
                SeparateTweetContent message countRetweets
                reTweetMap <- reTweetMap.Add(countRetweets,new retweetInterface(countRetweets,tweetId,userRetweeted,message))
                let User = tweetMap.[tweetId].userId
                let tweetStruct = new tweeetdb(tweetId,message,User,true,countRetweets,userRetweeted)
                userMap.[userRetweeted] <! TweetRegistered tweetStruct
                countRetweets <- countRetweets + 1
                let followerSet = updateTweets userRetweeted tweetStruct

                for i in followerSet do
                        i <! StoreNewTweets tweetStruct
                // callFanout <! Distribute (tweetStruct,(updateTweets userRetweeted tweetStruct))
            | DisplayFollowersOfUsers uid->
                printfn "Follower List."
                for user in followerMap.[uid] do
                    printf "%i " user
                printfn ""
    
            | DisplayFollowingofUSers uid->
                printfn "Following List."
                for user in followingMap.[uid] do
                    printf "%i " user
                printfn ""

            | Search data ->
                let Displaytweets (set:Set<int>) =
                    let mutable messageSet = Set.empty<tweeetdb>
                    for messageId in set do
                        if messageId > 0 then
                            messageSet <- messageSet.Add(new tweeetdb(messageId,tweetMap.[messageId].tweetMessage,tweetMap.[messageId].userId,false,0,0))
            
                    messageSet
    
                let mutable viewSet = Set.empty<tweeetdb>    
            
                if data.[0] = '#' && hashtagMap.ContainsKey(data) then
                    viewSet <- Displaytweets hashtagMap.[data]
                else if data.[0] = '@' && userTaggedMap.ContainsKey(data) then
                    viewSet <- Displaytweets userTaggedMap.[data]  
                else if textMap.ContainsKey(data) then
                    viewSet <- Displaytweets textMap.[data]
    
                mailbox.Sender() <! Display viewSet
    
            | ChangeServerState uId ->
                ifUserActive.[uId] <-(not ifUserActive.[uId])
                if ifUserActive.[uId] = true then
                    userMap.[uId] <! UpdateHomePage pendingTweets.[uId]
                    pendingTweets <- pendingTweets.Add(uId,Set.empty)

            |SimulatorsResponse (userfollower,userfollowing) ->
                followerMap <- userfollower
                followingMap <- userfollowing

            | UpdateFollow (tobefollowed,follower)->
                followingMap <- followingMap.Add(follower,followingMap.[follower].Add(tobefollowed))
                followerMap <- followerMap.Add(tobefollowed,followerMap.[tobefollowed].Add(follower))
                userMap.[tobefollowed] <! UpdateFollowingNew follower
            
            return! loop()
        }
        loop()
    
    


    let simulator  (mailbox : Actor<_>) =
        let mutable simulatorIsActive = new List<bool>()
        let mutable followerMap = Map.empty<int,Set<int>>
        let mutable followingMap = Map.empty<int,Set<int>>
        let mutable simulatorUserMap = Map.empty<int,IActorRef>
        let mutable idolSet = Set.empty<int>
        let arbitrary = System.Random()
        let mutable Users = 0
        let mutable valueOfzipfConstant = 0.0
        let mutable countAllTweetsByUsers = 0
        let rec loop() = actor {
            let! msg = mailbox.Receive()
            match msg with
            | InitializeSimulator (num)->
                Users <- num
                let zipf =
                    let mutable c = 0.0
                    for x = 1 to Users do
                        let i = x|> float
                        c <- c + (1.0/i)
                    (1.0/c)
                valueOfzipfConstant <- zipf
                simulatorIsActive.Add(true)
                for i = 1 to Users do
                    let mutable set = Set.empty
                    followerMap <- followerMap.Add(i, set)
                    followingMap <- followingMap.Add(i,set)
                    simulatorIsActive.Add(true)
    
                engine <! BeginEngine Users
                spawnUser <! CreateUsers Users
            | Reset ->
                countAllTweetsByUsers <- 0
            | Mapping (map) ->
                simulatorUserMap <- map
                callSimulator <! ConnectUsers
            | ConnectUsers ->
                let kpopIdols = (5*Users)/100
                while (idolSet.Count < kpopIdols) do
                    idolSet <- idolSet.Add(arbitrary.Next(1,Users+1))
                //printfn "Celebs: %i" idolSet.Count
            
            
                let addFollowers (userId,totalfollowers) =    
                    while (followerMap.[userId].Count <= totalfollowers) do
                        let rand = arbitrary.Next(1,Users+1)
                        if(rand <> userId && not (idolSet.Contains(rand))) then
                            followerMap <- followerMap.Add(userId,followerMap.[userId].Add(rand))
                            followingMap <- followingMap.Add(rand,followingMap.[rand].Add(userId))
                    simulatorUserMap.[userId] <! FollowersAndFollowingAdded (followerMap.[userId],1)
                    //printfn "User%i has %i followers." userId followerMap.[userId].Count
                
                let followerCount = (valueOfzipfConstant*(Users|>float)) |>int
                let LowerLimit = Math.Max(followerCount - (followerCount*10)/100,0)
                let UpperLimit = followerCount + (followerCount*10)/100
    
                let addIdolFollowers =
                    let mutable KpopIdolList = new List<int>(idolSet)
                    for userId in idolSet do
                        let celebfollowing = arbitrary.Next((idolSet.Count*10)/100,(idolSet.Count*20)/100)
                        while(followingMap.[userId].Count < celebfollowing) do
                            let mutable follow = KpopIdolList.[arbitrary.Next(0,idolSet.Count)]
                            if(follow <> userId) then
                                followingMap <- followingMap.Add(userId,followingMap.[userId].Add(follow))
                                simulatorUserMap.[follow] <! AddOneFollower_Following (userId, 1)
                                followerMap <- followerMap.Add(follow,followerMap.[follow].Add(userId))    
            
                for celeb in idolSet do
                    addFollowers (celeb,arbitrary.Next(LowerLimit,UpperLimit))
                        
                for userId = 1 to Users do
                    if not (idolSet.Contains(userId)) then
                        let followerCount =  ((valueOfzipfConstant*(Users|>float))|> int)/arbitrary.Next(2,12) + 1
                        let lowerLimit = Math.Max(followerCount - (followerCount*10)/100,0)
                        let upperLimit = followerCount + (followerCount*10)/100
                        addFollowers (userId,arbitrary.Next(lowerLimit,upperLimit))
    
                addIdolFollowers
    
                for user in simulatorUserMap do
                    user.Value <! FollowersAndFollowingAdded (followingMap.[user.Key],2)
                    //printfn "User%i follows %i people and is followed by %i people" user.Key followingMap.[user.Key].Count followerMap.[user.Key].Count  
    
                for user in followerMap do
                    TotalfollowersCount <- TotalfollowersCount + user.Value.Count
                engine <! SimulatorsResponse (followerMap,followingMap)
                Threading.Thread.Sleep(100)
                StopIter <- StopIter + 1
            | CountAllTweetsByUsers (testId)->
                countAllTweetsByUsers <- countAllTweetsByUsers + 1
                if testId = 1 then
                    if countAllTweetsByUsers >= 10* TotalfollowersCount-1 then
                        printfn "%i" countAllTweetsByUsers
                        flag<- true
                else if testId = 2 then
                    if countAllTweetsByUsers >= TotalfollowersCount then
                        flag<- true
            | BeginTweets (tweet,userId) ->
                //printfn "User%i tweeted %s" userId tweet
                simulatorUserMap.[userId] <! TweetRegisterOnEngine tweet
            | BeginRetweets (tweetId,userId)->
                simulatorUserMap.[userId] <! RetweetRegisterOnEngine tweetId
            | BeginTweetsForAllUsers (tweet) ->
                for i = 1 to Users do
                    simulatorUserMap.[i] <! TweetRegisterOnEngine tweet
            | DisplayFollower userId ->
                simulatorUserMap.[userId] <! DisplayUserProfile 1
            | DisplayFollowing userId ->
                simulatorUserMap.[userId] <! DisplayUserProfile 2
            | SimulateSearch (item,userId)->
                simulatorUserMap.[userId] <! SearchTweet item
            | BeginRetweetsForAllUsers ->
                for i = 1 to Users do
                    simulatorUserMap.[i] <! GenerateRandomTweetId
            | DisplayUserHome user ->
                simulatorUserMap.[user] <! DisplayHomePage
        
            | DisplayFollowerServer userId ->
                engine <! DisplayFollowersOfUsers userId

            | DisplayFollowingServer userId ->
                engine <! DisplayFollowingofUSers userId

            | SwitchState user ->
                if simulatorIsActive.[user] then
                    simulatorUserMap.[user] <! SignOut
                else
                    simulatorUserMap.[user] <! SignIn
                simulatorIsActive.[user] <- not simulatorIsActive.[user]
    
            
    
            return! loop()
        }
        loop()
    
    engine <- spawn system "twitterEngine" twitterEngine
    spawnUser <- spawn system "Generates-Users" usergenerator
    callSimulator <- spawn system "Call-Simulator" simulator
    actorrefmap <- actorrefmap.Add(0,engine)
    actorrefmap <- actorrefmap.Add(2,callSimulator)
    actorrefmap <- actorrefmap.Add(3,spawnUser)
    callSimulator <! InitializeSimulator (Users)
    
    let mutable check = true
    
    while StopIter = 0 do
        check <- false
    
    printfn "Twitter clone of %i Users Intitated: " Users
    let stopwatch = System.Diagnostics.Stopwatch.StartNew()
    for i = 0 to 9 do
        callSimulator <! BeginTweetsForAllUsers tweetList.[i]
    
    while not flag do
        check <- check
    
    stopwatch.Stop()
    printfn "Time taken to for circulating %i tweets : %f" (TotalfollowersCount*10) stopwatch.Elapsed.TotalMilliseconds
    printfn "---------------------------------------------------------------------------------------------------------"
    printfn "- - - -- - - - - - -- - - - - - -- - - - - - -- - - - - - - -- - - - -- - - - -- - - - -- - - - -- - - "
    
    callSimulator <! Reset
    flag<- false
    
    stopwatch.Reset()
    stopwatch.Start()
    callSimulator <! BeginRetweetsForAllUsers
    
    while not flag do
        check <- check
    stopwatch.Stop()
    printfn "Time taken to for circulating %i retweets : %f" (TotalfollowersCount) stopwatch.Elapsed.TotalMilliseconds
    printfn "---------------------------------------------------------------------------------------------------------"
    printfn "- - - -- - - - - - -- - - - - - -- - - - - - -- - - - - - - -- - - - -- - - - -- - - - -- - - - -- - - "
    
    for i = 1 to Users do 
        globalusermap.[i] <! SignOut
    startWebServer { defaultConfig with logger = Targets.create Verbose [||] } app

    0
    // let user1 = System.Random().Next(1,Users)
    // printfn "Followers of User%i : " user1
    // callSimulator <! DisplayFollower user1
    // Threading.Thread.Sleep(100)
    // while StopIter < 1 do
    //     check <- false
    // printfn "---------------------------------------------------------------------------------------------------------"
    // printfn "- - - -- - - - - - -- - - - - - -- - - - - - -- - - - - - - -- - - - -- - - - -- - - - -- - - - -- - - "
    // printfn "Following of User%i : " random
    // engine <! DisplayFollowingofUSers random
    // Threading.Thread.Sleep(100)
    // printfn "- - - -- - - - - - -- - - - - - -- - - - - - -- - - - - - - -- - - - -- - - - -- - - - -- - - - -- - - "
    // printfn "User %i goes Offline" random
    // printfn " "
    // callSimulator <! SwitchState random
    // printfn "User %i makes a tweet, user%i's Timeline doesn't get updated as the user is offline" user1 random
    // //Threading.Thread.Sleep(1000)
    // callSimulator <! BeginTweets (sprintf "DJ Turn it up #YellowClaw @USer%i YOu Can't see me Satoshi Nakamoto" random,user1)
    // //Threading.Thread.Sleep(1000)
    // printfn " "
    // printfn "User%i's HomePage when it is offline" random
    // callSimulator <! DisplayUserHome random
    // Threading.Thread.Sleep(500)
    // printfn "---------------------------------------------------------------------------------------------------------"
    // printfn "- - - -- - - - - - -- - - - - - -- - - - - - -- - - - - - - -- - - - -- - - - -- - - - -- - - - -- - - "
    // printfn "User %i comes Online" random
    // callSimulator <! SwitchState random
    // Threading.Thread.Sleep(10)
    // printfn "User%i's HomePage when it is online" random
    // callSimulator <! DisplayUserHome random
    // Threading.Thread.Sleep(500)
    // printfn "---------------------------------------------------------------------------------------------------------"
    // printfn "- - - -- - - - - - -- - - - - - -- - - - - - -- - - - - - - -- - - - -- - - - -- - - - -- - - - -- - - "
    // stopwatch.Reset()
    // stopwatch.Start()
    // printfn "Searching for keyword Bangtan"
    // callSimulator <! SimulateSearch ("Bangtan", user1)
    // stopwatch.Stop()
    // printfn "Time taken(in ms) to search Bangtan in system is: %f" stopwatch.Elapsed.TotalMilliseconds
    // Threading.Thread.Sleep(100)
    // printfn "---------------------------------------------------------------------------------------------------------"
    // printfn "- - - -- - - - - - -- - - - - - -- - - - - - -- - - - - - - -- - - - -- - - - -- - - - -- - - - -- - - "
    // stopwatch.Reset()
    // stopwatch.Start()
    // printfn "Searching for usertagged: @USer1"
    // callSimulator <! SimulateSearch ("@USer1", user1)
    // stopwatch.Stop()
    // printfn "Time taken(in ms) to search @User1 in system is: %f" stopwatch.Elapsed.TotalMilliseconds
    // Threading.Thread.Sleep(100)
    // printfn "---------------------------------------------------------------------------------------------------------"
    // printfn "- - - -- - - - - - -- - - - - - -- - - - - - -- - - - - - - -- - - - -- - - - -- - - - -- - - - -- - - "
    // stopwatch.Reset()
    // stopwatch.Start()
    // printfn "Searching for hashtag: #BTSArmy"
    // callSimulator <! SimulateSearch ("#BTSArmy", user1)
    // stopwatch.Stop()
    // printfn "Time taken(in ms) to search #BTSArmy in system is: %f" stopwatch.Elapsed.TotalMilliseconds
    // Threading.Thread.Sleep(100)
    // printfn "---------------------------------------------------------------------------------------------------------"
    // printfn "- - - -- - - - - - -- - - - - - -- - - - - - -- - - - - - - -- - - - -- - - - -- - - - -- - - - -- - - "
    
    // Threading.Thread.Sleep(2000)
    
    //#time "on"
    
