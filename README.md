# VaultVid
A video on demand self-hostable platform.

Note: This is a work in progress, the following notes will be applicable to the first release.

## Features
* For each instance, you can decide who can upload videos (only certain users, all users).
* Selected users can upload videos from a dashboard.
* Users can upvote, downvote and comment on videos.
* Users can create playlists.
* Operators have flexibility regarding transcoding.

## Transcoding
Soon as a video is uploaded, the main server is going to enqueue transcoding to all supported resolutions and encoding.
The transcoding work is done by a worker that will receive items on the queue and will process each possible encode combination, queue has retry logic that it will retry encode if it fails.
Operator decides how many services the worker has available for transcoding.
