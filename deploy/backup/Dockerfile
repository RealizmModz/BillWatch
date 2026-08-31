FROM postgres:17-alpine

RUN apk add --no-cache \
        ca-certificates \
        jq \
        restic \
    && mkdir --parents /cache /repository /work \
    && chown --recursive 1654:1654 /cache /repository /work

COPY backup.sh /usr/local/bin/billwatch-backup

RUN chmod 0555 /usr/local/bin/billwatch-backup

ENV HOME=/work \
    RESTIC_CACHE_DIR=/cache

USER 1654:1654

ENTRYPOINT ["/usr/local/bin/billwatch-backup"]
CMD ["backup"]
