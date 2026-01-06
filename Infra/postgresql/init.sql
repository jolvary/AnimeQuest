CREATE TABLE users (
  id UUID PRIMARY KEY,
  display_name TEXT NOT NULL,
  created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE TABLE anime (
  id BIGSERIAL PRIMARY KEY,
  provider TEXT NOT NULL,             
  provider_id TEXT NOT NULL,
  title TEXT NOT NULL,
  genres TEXT[] NOT NULL DEFAULT '{}',
  episodes INT,
  year INT,
  trailer_youtube_id TEXT,
  created_at TIMESTAMP NOT NULL DEFAULT NOW(),
  UNIQUE(provider, provider_id)
);

CREATE TABLE quests (
  id BIGSERIAL PRIMARY KEY,
  code TEXT NOT NULL UNIQUE,
  title TEXT NOT NULL,
  description TEXT NOT NULL,
  requirements JSONB NOT NULL,
  rewards JSONB NOT NULL,
  created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE TABLE user_quests (
  user_id UUID NOT NULL REFERENCES users(id),
  quest_id BIGINT NOT NULL REFERENCES quests(id),
  status TEXT NOT NULL,               
  progress JSONB NOT NULL DEFAULT '{}',
  updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
  PRIMARY KEY (user_id, quest_id)
);

CREATE TABLE watch_entries (
  user_id UUID NOT NULL REFERENCES users(id),
  anime_id BIGINT NOT NULL REFERENCES anime(id),
  status TEXT NOT NULL,               
  score INT,
  episodes_watched INT NOT NULL DEFAULT 0,
  updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
  PRIMARY KEY (user_id, anime_id)
);

CREATE TABLE user_achievements (
  user_id UUID NOT NULL REFERENCES users(id),
  achievement_code TEXT NOT NULL,
  achieved_at TIMESTAMP NOT NULL DEFAULT NOW(),
  PRIMARY KEY (user_id, achievement_code)
);