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
As soon as a video is uploaded, the web app stores the source in object storage, saves the row in
the `Pending` state and enqueues a transcode job on RabbitMQ. A separate worker service consumes
the queue and produces an HLS ladder with ffmpeg.

The ladder runs 360p, 480p, 720p, 1080p, 1440p and 2160p, and is capped at the source
resolution - a 1920x1080 upload gets four renditions and is never upscaled to 1440p or 4K.

360p and 480p are encoded first. The moment both exist the video flips to `Ready` and becomes
watchable; the higher rungs continue afterwards and are added to the HLS master playlist as they
finish, so the quality menu fills in while the video is already playing.

The two halves of the ladder travel on **separate queues**. The required rungs - the short jobs a
viewer is actually waiting on - go to `vaultvid.transcode.required`; once they finish and the video
is live, the worker enqueues the rest onto `vaultvid.transcode.optional`. Each queue has its own
worker deployment, so a new upload can never wait behind somebody else's 4K encode.

Failed jobs are retried with a backoff before the video is marked `Failed` with a reason. Jobs
that never reached the broker, or whose worker died mid-encode, are re-queued by a sweeper in the
web app.

The workers are stateless, so an operator decides how much capacity each lane gets:

```bash
docker compose up --scale transcoder=4 --scale transcoder-optional=2
```

Starving the optional lane only delays the higher resolutions; uploads still become watchable at
the usual speed.

Playback goes through `/media/{publicId}/...` in the web app rather than presigned storage URLs:
an HLS player follows relative URIs to child playlists and segments, which cannot carry a
signature. Thumbnails are still served straight from object storage with presigned URLs.
