# Build from the repository root: docker build -f services/grilled/Dockerfile .
FROM node:24-alpine AS build
WORKDIR /app
COPY services/grilled/package*.json ./
RUN npm ci
COPY services/grilled/tsconfig.json ./
COPY services/grilled/src ./src
RUN npm run build && npm prune --omit=dev

FROM node:24-alpine
WORKDIR /app
ENV NODE_ENV=production PORT=8080 GRILLED_DB_PATH=/data/grilled.db
COPY --from=build /app/package.json ./
COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/dist ./dist
# The SQLite file must live on a mounted volume, or it disappears with the container.
RUN mkdir /data && chown node /data
VOLUME /data
EXPOSE 8080
USER node
CMD ["node", "dist/index.js"]
